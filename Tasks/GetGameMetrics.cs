#region

using System.Collections.Concurrent;
using AGC_Management.Entities.Metrics;
using AGC_Management.Services;

#endregion

namespace AGC_Management.Tasks;

/// <summary>
///     Samples who is playing what, once a minute, the same way <see cref="GetVoiceMetrics" /> samples
///     voice: one row per member and minute, so a plain COUNT(*) is the playtime and every existing
///     metric query works on it unchanged. It also feeds the catalogue and triggers the selfrole
///     detection, so there is one place that knows what somebody is playing instead of two.
/// </summary>
public static class GetGameMetrics
{
    private static readonly ConcurrentDictionary<ulong, string> LastGame = new();

    public static async Task LaunchLoops()
    {
        await Task.Delay(TimeSpan.FromSeconds(30));

        try
        {
            await GameCatalogService.RepairIdentitiesAsync();
        }
        catch (Exception e)
        {
            CurrentApplication.Logger.Error(e, "Spielkatalog: Neunummerierung fehlgeschlagen");
        }

        while (true)
        {
            try
            {
                if (CurrentApplication.TargetGuild is not null && MemberCacheService.InitialDownloadComplete)
                {
                    await SampleAsync();
                    await RollupAsync();
                }
            }
            catch (Exception e)
            {
                CurrentApplication.Logger.Error(e, "Spielmetrik: Durchlauf fehlgeschlagen");
            }

            await Task.Delay(TimeSpan.FromSeconds(60));
        }
    }

    private static async Task SampleAsync()
    {
        var guild = CurrentApplication.TargetGuild;
        if (guild is null) return;

        var playing = new List<(DiscordMember Member, GameCatalogEntry Entry)>();
        var seen = new Dictionary<string, GameCatalogEntry>(StringComparer.Ordinal);

        foreach (var member in guild.Members.Values)
        {
            if (member.IsBot) continue;

            var activity = member.Presence?.Activities?
                .FirstOrDefault(a => a.ActivityType == ActivityType.Playing &&
                                     !string.IsNullOrWhiteSpace(a.Name));
            if (activity is null)
            {
                LastGame.TryRemove(member.Id, out _);
                continue;
            }

            var applicationId = activity.RichPresence?.Application?.Id ?? 0;
            var entry = await GameCatalogService.ResolveAsync(activity.Name, applicationId);
            if (entry is null) continue;

            seen[entry.Key] = entry;
            playing.Add((member, entry));
        }

        if (playing.Count == 0) return;

        await WriteSamplesAsync(playing);
        await GameCatalogService.TouchAsync(seen.Values);
        await DetectRolesAsync(playing);
    }

    private static async Task WriteSamplesAsync(List<(DiscordMember Member, GameCatalogEntry Entry)> playing)
    {
        var db = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        await using var cmd = db.CreateCommand(
            "INSERT INTO metrics_activity (userid, activityname, activityid, timestamp) " +
            "VALUES (@userid, @activityname, @activityid, @timestamp)");
        var userid = cmd.Parameters.AddWithValue("userid", 0L);
        var activityname = cmd.Parameters.AddWithValue("activityname", "");
        var activityid = cmd.Parameters.AddWithValue("activityid", 0L);
        cmd.Parameters.AddWithValue("timestamp", timestamp);

        foreach (var (member, entry) in playing)
        {
            userid.Value = (long)member.Id;
            activityname.Value = entry.DisplayName;
            activityid.Value = entry.ActivityId;
            await cmd.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    ///     Only members who switched games since the last pass are looked at, so a server full of people
    ///     playing the same thing all evening costs no database reads at all.
    /// </summary>
    private static async Task DetectRolesAsync(List<(DiscordMember Member, GameCatalogEntry Entry)> playing)
    {
        foreach (var (member, entry) in playing)
        {
            if (LastGame.TryGetValue(member.Id, out var previous) &&
                string.Equals(previous, entry.Key, StringComparison.Ordinal))
                continue;

            LastGame[member.Id] = entry.Key;

            try
            {
                await SelfroleService.AutoAssignAsync(member, entry);
            }
            catch (Exception e)
            {
                CurrentApplication.Logger.Error(e, "Selfroles: Erkennung fuer {User} fehlgeschlagen", member.Id);
            }
        }
    }

    /// <summary>
    ///     Folds yesterday's samples into one row per member, game and day, then drops what is past its
    ///     retention. The watermark makes this incremental, so the pass does not re-read every sample
    ///     that is still around every single day.
    /// </summary>
    private static async Task RollupAsync()
    {
        var todayStart = DateTimeOffset.UtcNow.Date.ToUniversalTime();
        var until = new DateTimeOffset(todayStart, TimeSpan.Zero).ToUnixTimeSeconds();

        var raw = await RuntimeSettings.GetAsync(GameCatalogService.Section, "LastRollupUntil");
        var from = long.TryParse(raw, out var parsed) ? parsed : 0;
        if (until <= from) return;

        var db = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

        await using (var cmd = db.CreateCommand(
                         "INSERT INTO metrics_activitydaily (activityid, userid, day, minutes) " +
                         "SELECT activityid, userid, (to_timestamp(timestamp) AT TIME ZONE 'UTC')::date AS day, " +
                         "COUNT(*) " +
                         "FROM metrics_activity WHERE timestamp >= @from AND timestamp < @until " +
                         "GROUP BY activityid, userid, day " +
                         "ON CONFLICT (activityid, userid, day) DO UPDATE SET minutes = EXCLUDED.minutes"))
        {
            cmd.CommandTimeout = 900;
            cmd.Parameters.AddWithValue("from", from);
            cmd.Parameters.AddWithValue("until", until);
            await cmd.ExecuteNonQueryAsync();
        }

        await RuntimeSettings.SetAsync(GameCatalogService.Section, "LastRollupUntil", until.ToString());
        await PruneAsync(db);
    }

    private static async Task PruneAsync(NpgsqlDataSource db)
    {
        var sampleDays = await RuntimeSettings.GetIntAsync(GameCatalogService.Section, "SampleRetentionDays", 90);
        if (sampleDays > 0)
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-sampleDays).ToUnixTimeSeconds();
            await using var cmd = db.CreateCommand("DELETE FROM metrics_activity WHERE timestamp < @cutoff");
            cmd.CommandTimeout = 900;
            cmd.Parameters.AddWithValue("cutoff", cutoff);
            var removed = await cmd.ExecuteNonQueryAsync();
            if (removed > 0)
                CurrentApplication.Logger.Information("Spielmetrik: {Count} Rohproben verfallen", removed);
        }

        var dailyDays = await RuntimeSettings.GetIntAsync(GameCatalogService.Section, "DailyRetentionDays", 0);
        if (dailyDays <= 0) return;

        await using var daily = db.CreateCommand("DELETE FROM metrics_activitydaily WHERE day < @cutoff");
        daily.CommandTimeout = 900;
        daily.Parameters.AddWithValue("cutoff", DateTime.UtcNow.Date.AddDays(-dailyDays));
        await daily.ExecuteNonQueryAsync();
    }
}
