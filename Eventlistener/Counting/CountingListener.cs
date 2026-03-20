#region

using DisCatSharp.ApplicationCommands;
using Microsoft.Extensions.DependencyInjection;

#endregion

namespace AGC_Management.Eventlistener.Counting;

[EventHandler]
public sealed class CountingListener : ApplicationCommandsModule
{
    private const ulong CountingChannelId = 767133740421218314;

    [Event]
    private Task MessageCreated(DiscordClient client, MessageCreateEventArgs args)
    {
        if (args.Author.IsBot) return Task.CompletedTask;

        _ = Task.Run(async () =>
        {
            if (CurrentApplication.TargetGuild == null) return;
            if (args.Channel.Type == ChannelType.Private) return;
            if (args.Guild.Id != CurrentApplication.TargetGuild.Id) return;
            if (args.Channel.Id != CountingChannelId) return;

            var message = args.Message;
            long userId = (long)args.Author.Id;

            var parts = message.Content.Trim().Split();
            if (parts.Length == 0 || !long.TryParse(parts[0], out long inputNumber)) return;

            var conn = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

            long lastNumber, lastUser;
            await using (var cmd = conn.CreateCommand("SELECT lastnumber, lastuser FROM counting LIMIT 1"))
            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                if (!await reader.ReadAsync()) return;
                lastNumber = reader.GetInt64(0);
                lastUser = reader.GetInt64(1);
            }

            long currentNumber = lastNumber + 1;
            bool sameUser = lastUser == userId;
            bool wrongNumber = currentNumber != inputNumber;

            if (sameUser || wrongNumber)
            {
                if (lastNumber >= 10)
                {
                    decimal saves = await GetUserSaves(conn, userId);
                    if (saves >= 1m)
                    {
                        await ConsumeOneSave(conn, userId);
                        await using (var cmd = conn.CreateCommand("UPDATE counting SET lastuser = 0"))
                            await cmd.ExecuteNonQueryAsync();

                        await UpdateCountCounter(conn, userId);

                        try { await message.CreateReactionAsync(DiscordEmoji.FromGuildEmote(client, 962007085426556989)); } catch { }
                        try { await message.CreateReactionAsync(DiscordEmoji.FromGuildEmote(client, 962007095882969179)); } catch { }

                        string reason = sameUser
                            ? "__Du darfst nicht 2x hintereinander zählen!__"
                            : "Die Zahl ist falsch.";
                        await message.RespondAsync(
                            $"{args.Author.Mention} {reason} Du hast die Reihe bei {lastNumber} zerstört.\n" +
                            $"**__Du aber hast einen deiner Sicherungen benutzt und es geht weiter!__**, **Die nächste Zahl ist {currentNumber}.**");

                        await TryUpdateHighscore(conn, inputNumber, userId);
                        return;
                    }
                }

                await using (var cmd = conn.CreateCommand("UPDATE counting SET lastnumber = 0, lastuser = 0"))
                    await cmd.ExecuteNonQueryAsync();

                await UpdateFailCounter(conn, userId);

                try { await message.CreateReactionAsync(DiscordEmoji.FromGuildEmote(client, 961655533432078356)); }
                catch { await message.CreateReactionAsync(DiscordEmoji.FromUnicode("❌")); }

                string failReason = sameUser
                    ? "__Du darfst nicht 2x hintereinander zählen!__"
                    : "Die Zahl ist falsch.";
                await message.RespondAsync(
                    $"{args.Author.Mention} {failReason} Du hast die Reihe bei {lastNumber} zerstört. **Die nächste Zahl ist 1.**");
                return;
            }

            await using (var cmd = conn.CreateCommand("UPDATE counting SET lastnumber = lastnumber + 1, lastuser = @uid"))
            {
                cmd.Parameters.AddWithValue("uid", userId);
                await cmd.ExecuteNonQueryAsync();
            }

            await UpdateCountCounter(conn, userId);

            if (inputNumber == 100)
                await message.CreateReactionAsync(DiscordEmoji.FromUnicode("💯"));

            bool newHighscore = await TryUpdateHighscore(conn, inputNumber, userId);
            if (newHighscore)
            {
                try { await message.CreateReactionAsync(DiscordEmoji.FromGuildEmote(client, 961655533432078336)); }
                catch { await message.CreateReactionAsync(DiscordEmoji.FromUnicode("🏆")); }
            }
            else
            {
                try { await message.CreateReactionAsync(DiscordEmoji.FromGuildEmote(client, 961655533478236170)); }
                catch { await message.CreateReactionAsync(DiscordEmoji.FromUnicode("✅")); }
            }
        });

        return Task.CompletedTask;
    }

    private static async Task<decimal> GetUserSaves(NpgsqlDataSource conn, long userId)
    {
        await using var cmd = conn.CreateCommand("SELECT saves FROM countsave WHERE userid = @uid");
        cmd.Parameters.AddWithValue("uid", userId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync() && !reader.IsDBNull(0))
            return reader.GetDecimal(0);
        return 0m;
    }

    private static async Task ConsumeOneSave(NpgsqlDataSource conn, long userId)
    {
        await using var cmd = conn.CreateCommand("UPDATE countsave SET saves = saves - 1 WHERE userid = @uid");
        cmd.Parameters.AddWithValue("uid", userId);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task UpdateCountCounter(NpgsqlDataSource conn, long userId)
    {
        bool exists = false;
        await using (var checkCmd = conn.CreateCommand("SELECT 1 FROM countcounter WHERE userid = @uid"))
        {
            checkCmd.Parameters.AddWithValue("uid", userId);
            await using var reader = await checkCmd.ExecuteReaderAsync();
            exists = await reader.ReadAsync();
        }

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (!exists)
        {
            await using var cmd = conn.CreateCommand("INSERT INTO countcounter (userid, counter, timestamps) VALUES (@uid, 1, @ts)");
            cmd.Parameters.AddWithValue("uid", userId);
            cmd.Parameters.AddWithValue("ts", now);
            await cmd.ExecuteNonQueryAsync();
        }
        else
        {
            await using var cmd = conn.CreateCommand("UPDATE countcounter SET counter = counter + 1, timestamps = @ts WHERE userid = @uid");
            cmd.Parameters.AddWithValue("ts", now);
            cmd.Parameters.AddWithValue("uid", userId);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task UpdateFailCounter(NpgsqlDataSource conn, long userId)
    {
        bool exists = false;
        await using (var checkCmd = conn.CreateCommand("SELECT 1 FROM countingfails WHERE userid = @uid"))
        {
            checkCmd.Parameters.AddWithValue("uid", userId);
            await using var reader = await checkCmd.ExecuteReaderAsync();
            exists = await reader.ReadAsync();
        }

        if (!exists)
        {
            await using var cmd = conn.CreateCommand("INSERT INTO countingfails (userid, counter) VALUES (@uid, 1)");
            cmd.Parameters.AddWithValue("uid", userId);
            await cmd.ExecuteNonQueryAsync();
        }
        else
        {
            await using var cmd = conn.CreateCommand("UPDATE countingfails SET counter = counter + 1 WHERE userid = @uid");
            cmd.Parameters.AddWithValue("uid", userId);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task<bool> TryUpdateHighscore(NpgsqlDataSource conn, long inputNumber, long userId)
    {
        long highscore = 0;
        await using (var cmd = conn.CreateCommand("SELECT number FROM countinghighscore LIMIT 1"))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            if (await reader.ReadAsync())
                highscore = reader.GetInt64(0);
        }

        if (inputNumber > highscore)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            await using var cmd = conn.CreateCommand("UPDATE countinghighscore SET number = @n, userid = @uid, timestamps = @ts");
            cmd.Parameters.AddWithValue("n", inputNumber);
            cmd.Parameters.AddWithValue("uid", userId);
            cmd.Parameters.AddWithValue("ts", now);
            await cmd.ExecuteNonQueryAsync();
            return true;
        }

        return false;
    }
}
