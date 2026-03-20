#region

using DisCatSharp.ApplicationCommands;
using Microsoft.Extensions.DependencyInjection;

#endregion

namespace AGC_Management.Eventlistener;

[EventHandler]
public sealed class VorstellungscooldownListener : ApplicationCommandsModule
{
    private const ulong VorstellungsChannelId = 784909775615295508;
    private const long CooldownSeconds = 864000; // 10 Tage

    [Event]
    private Task MessageCreated(DiscordClient client, MessageCreateEventArgs args)
    {
        if (args.Author.IsBot) return Task.CompletedTask;

        _ = Task.Run(async () =>
        {
            if (CurrentApplication.TargetGuild == null) return;
            if (args.Channel.Type == ChannelType.Private) return;
            if (args.Guild.Id != CurrentApplication.TargetGuild.Id) return;
            if (args.Channel.Id != VorstellungsChannelId) return;
            if (args.Message.MessageType != MessageType.Default && args.Message.MessageType != MessageType.Reply) return;

            long userId = (long)args.Author.Id;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var conn = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

            long? lastPost = null;
            await using (var cmd = conn.CreateCommand("SELECT time FROM vorstellungscooldown WHERE user_id = @uid"))
            {
                cmd.Parameters.AddWithValue("uid", userId);
                await using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                    lastPost = reader.GetInt64(0);
            }

            if (lastPost is null)
            {
                await using var cmd = conn.CreateCommand("INSERT INTO vorstellungscooldown (user_id, time) VALUES (@uid, @time)");
                cmd.Parameters.AddWithValue("uid", userId);
                cmd.Parameters.AddWithValue("time", now);
                await cmd.ExecuteNonQueryAsync();
                return;
            }

            long elapsed = now - lastPost.Value;
            if (elapsed > CooldownSeconds)
            {
                await using var cmd = conn.CreateCommand("UPDATE vorstellungscooldown SET time = @time WHERE user_id = @uid");
                cmd.Parameters.AddWithValue("time", now);
                cmd.Parameters.AddWithValue("uid", userId);
                await cmd.ExecuteNonQueryAsync();
                return;
            }

            await args.Message.DeleteAsync("Vorstellungscooldown!");

            long remaining = CooldownSeconds - elapsed;
            long days = remaining / 86400;
            long hours = (remaining % 86400) / 3600;
            long minutes = (remaining % 3600) / 60;

            var timeParts = new List<string>();
            if (days > 0) timeParts.Add($"{days} Tag{(days != 1 ? "e" : "")}");
            if (hours > 0) timeParts.Add($"{hours} Stunde{(hours != 1 ? "n" : "")}");
            if (minutes > 0) timeParts.Add($"{minutes} Minute{(minutes != 1 ? "n" : "")}");
            string readableTime = string.Join(", ", timeParts);

            CurrentApplication.Logger.Information(
                "Vorstellungscooldown: Nachricht von {User} gelöscht (verbleibend: {Time})",
                args.Author.Username, readableTime);

            try
            {
                await args.Author.SendMessageAsync(
                    $"Hey langsam! Du kannst erst wieder in **{readableTime}** eine Vorstellung posten.");
            }
            catch { }
        });

        return Task.CompletedTask;
    }
}
