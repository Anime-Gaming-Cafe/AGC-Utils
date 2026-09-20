#region

using AGC_Management.Entities.BanRequests;
using AGC_Management.Enums.BanRequests;
using AGC_Management.Utils;
using DisCatSharp.EventArgs;
using DisCatSharp.Enums.Core;
using DisCatSharp.Interactivity;
using DisCatSharp.Interactivity.Extensions;

#endregion

namespace AGC_Management.Services;

/// <summary>
///     Escalation ladder for ban requests. Replaces the dead presence filter: instead of guessing once
///     who is online, the circle of pinged people grows over time until someone presses a button.
/// </summary>
public static class BanRequestService
{
    private const string Section = "BanRequests";

    /// <summary>Excluded from every individual ping, historically hardcoded in both request commands.</summary>
    private const ulong PingExcludedUserId = 441192596325531648;

    #region Settings

    public static Task<int> GetPingLifetimeSecondsAsync()
    {
        return RuntimeSettings.GetIntAsync(Section, "PingLifetimeSeconds", 0);
    }

    public static Task SetPingLifetimeSecondsAsync(int seconds)
    {
        return RuntimeSettings.SetAsync(Section, "PingLifetimeSeconds", seconds.ToString());
    }

    public static Task<int> GetRequestTimeoutHoursAsync()
    {
        return RuntimeSettings.GetIntAsync(Section, "RequestTimeoutHours", 6);
    }

    public static Task SetRequestTimeoutHoursAsync(int hours)
    {
        return RuntimeSettings.SetAsync(Section, "RequestTimeoutHours", hours.ToString());
    }

    public static Task<int> GetEstimateWindowMinutesAsync()
    {
        return RuntimeSettings.GetIntAsync(Section, "EstimateWindowMinutes", 15);
    }

    public static Task SetEstimateWindowMinutesAsync(int minutes)
    {
        return RuntimeSettings.SetAsync(Section, "EstimateWindowMinutes", minutes.ToString());
    }

    #endregion

    #region Stages

    public static async Task<List<BanRequestStage>> GetStagesAsync()
    {
        var stages = new List<BanRequestStage>();
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = con.CreateCommand(
            "SELECT stage_id, position, enabled, delay_minutes, target, role_id FROM banrequest_stages " +
            "ORDER BY position, delay_minutes");

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            stages.Add(new BanRequestStage
            {
                StageId = reader.GetString(0),
                Position = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                Enabled = reader.IsDBNull(2) || reader.GetBoolean(2),
                DelayMinutes = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                Target = ParseTarget(reader.IsDBNull(4) ? "role" : reader.GetString(4)),
                RoleId = reader.IsDBNull(5) ? 0 : (ulong)reader.GetInt64(5)
            });

        return stages;
    }

    private static BanRequestStageTarget ParseTarget(string raw)
    {
        return Enum.TryParse<BanRequestStageTarget>(raw, true, out var parsed)
            ? parsed
            : BanRequestStageTarget.Role;
    }

    /// <summary>
    ///     Replaces the whole ladder in one go - the dashboard edits it as a list, so a diff would only
    ///     add bookkeeping for a handful of rows.
    /// </summary>
    public static async Task SaveStagesAsync(IEnumerable<BanRequestStage> stages)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var connection = await con.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await using (var clear = new NpgsqlCommand("DELETE FROM banrequest_stages", connection, transaction))
        {
            await clear.ExecuteNonQueryAsync();
        }

        var position = 0;
        foreach (var stage in stages)
        {
            await using var cmd = new NpgsqlCommand(
                "INSERT INTO banrequest_stages (stage_id, position, enabled, delay_minutes, target, role_id) " +
                "VALUES (@stage_id, @position, @enabled, @delay_minutes, @target, @role_id)",
                connection, transaction);
            cmd.Parameters.AddWithValue("stage_id",
                string.IsNullOrWhiteSpace(stage.StageId) ? ToolSet.GenerateCaseID() : stage.StageId);
            cmd.Parameters.AddWithValue("position", position++);
            cmd.Parameters.AddWithValue("enabled", stage.Enabled);
            cmd.Parameters.AddWithValue("delay_minutes", Math.Max(stage.DelayMinutes, 0));
            cmd.Parameters.AddWithValue("target", stage.Target.ToString().ToLowerInvariant());
            cmd.Parameters.AddWithValue("role_id", (long)stage.RoleId);
            await cmd.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    #endregion

    #region Escalation

    /// <summary>
    ///     Waits for someone with ban permission to press a button and widens the ping along the way.
    ///     Editing a message does not notify anyone, so every rung edits the request for the visible
    ///     state and additionally sends a short reply that carries the actual mention.
    /// </summary>
    public static async Task<InteractivityResult<ComponentInteractionCreateEventArgs>> WaitWithEscalationAsync(
        DiscordMessage requestMessage, Func<ComponentInteractionCreateEventArgs, bool> predicate,
        List<DiscordMember> banPermittedStaff)
    {
        var interactivity = CurrentApplication.DiscordClient.GetInteractivity();
        var timeoutHours = Math.Clamp(await GetRequestTimeoutHoursAsync(), 1, 24);
        var waitTask = interactivity.WaitForButtonAsync(requestMessage, predicate, TimeSpan.FromHours(timeoutHours));

        // Ordered by time, not by the order the dashboard lists them in - a rung whose minute has
        // already passed would otherwise fire out of turn.
        var stages = (await GetStagesAsync())
            .Where(s => s.Enabled)
            .OrderBy(s => s.DelayMinutes)
            .ThenBy(s => s.Position)
            .ToList();
        if (stages.Count == 0)
        {
            await PingAsync(requestMessage, FallbackMention());
            return await waitTask;
        }

        var window = TimeSpan.FromMinutes(Math.Max(await GetEstimateWindowMinutesAsync(), 1));
        var started = DateTimeOffset.UtcNow;
        using var pending = new CancellationTokenSource();

        foreach (var stage in stages)
        {
            var due = started.AddMinutes(stage.DelayMinutes) - DateTimeOffset.UtcNow;
            if (due > TimeSpan.Zero)
            {
                await Task.WhenAny(waitTask, Task.Delay(due, pending.Token));
                if (waitTask.IsCompleted) break;
            }

            var mention = await BuildMentionAsync(stage, banPermittedStaff, window);
            if (!string.IsNullOrWhiteSpace(mention)) await PingAsync(requestMessage, mention);
        }

        await pending.CancelAsync();
        return await waitTask;
    }

    private static async Task<string> BuildMentionAsync(BanRequestStage stage,
        List<DiscordMember> banPermittedStaff, TimeSpan window)
    {
        switch (stage.Target)
        {
            case BanRequestStageTarget.Role:
                return stage.RoleId == 0 ? "" : $"<@&{stage.RoleId}>";
            case BanRequestStageTarget.All:
                return JoinMentions(banPermittedStaff);
            case BanRequestStageTarget.Estimated:
                return JoinMentions(await AvailabilityService.FilterAvailableAsync(banPermittedStaff, window));
            default:
                return "";
        }
    }

    private static string JoinMentions(IEnumerable<DiscordMember> members)
    {
        return string.Join(" ", members.Where(m => m.Id != PingExcludedUserId).Select(m => m.Mention));
    }

    private static string FallbackMention()
    {
        try
        {
            var config = BotConfig.GetConfig()["ServerConfig"];
            return $"Kein Moderator erreichbar | <@&{config["AdminRoleId"]}> | <@&{config["ModRoleId"]}>";
        }
        catch (Exception)
        {
            return "";
        }
    }

    private static async Task PingAsync(DiscordMessage requestMessage, string mention)
    {
        if (GlobalProperties.DebugMode)
        {
            await SafeEditAsync(requestMessage, "DEBUG MODE AKTIV | Kein Ping wird ausgeführt");
            return;
        }

        await SafeEditAsync(requestMessage, mention);

        try
        {
            var ping = await requestMessage.Channel.SendMessageAsync(
                new DiscordMessageBuilder().WithContent(mention).WithReply(requestMessage.Id));
            var lifetime = Math.Clamp(await GetPingLifetimeSecondsAsync(), 0, 3600);
            _ = DeleteAfterAsync(ping, lifetime);
        }
        catch (Exception e)
        {
            CurrentApplication.Logger.Error(e, "Failed to send ban request ping");
        }
    }

    private static async Task SafeEditAsync(DiscordMessage message, string content)
    {
        try
        {
            await message.ModifyAsync(builder => builder.WithContent(content), ModifyMode.Update);
        }
        catch (Exception e)
        {
            CurrentApplication.Logger.Error(e, "Failed to update ban request content");
        }
    }

    private static async Task DeleteAfterAsync(DiscordMessage message, int seconds)
    {
        try
        {
            if (seconds > 0) await Task.Delay(TimeSpan.FromSeconds(seconds));
            await message.DeleteAsync();
        }
        catch (Exception e)
        {
            CurrentApplication.Logger.Error(e, "Failed to delete ban request ping");
        }
    }

    #endregion
}
