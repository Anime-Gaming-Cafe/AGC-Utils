#region

using AGC_Management.Attributes;
using Microsoft.Extensions.DependencyInjection;

#endregion

namespace AGC_Management.Commands.Fun;

public sealed class CountingCommands : BaseCommandModule
{
    [Command("countinginfo")]
    [Aliases("countingstats")]
    [RequireDatabase]
    [Description("Zeigt Informationen über das Counting-System an.")]
    public async Task CountingInfo(CommandContext ctx)
    {
        var conn = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

        long lastNumber, lastUser;
        await using (var cmd = conn.CreateCommand("SELECT lastnumber, lastuser FROM counting LIMIT 1"))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            if (!await reader.ReadAsync())
            {
                await ctx.RespondAsync("Die Counting-Daten konnten nicht abgerufen werden.");
                return;
            }
            lastNumber = reader.GetInt64(0);
            lastUser = reader.GetInt64(1);
        }

        long hsNumber, hsUser, hsTimestamp;
        await using (var cmd = conn.CreateCommand("SELECT number, userid, timestamps FROM countinghighscore LIMIT 1"))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            if (!await reader.ReadAsync())
            {
                await ctx.RespondAsync("Kein Highscore vorhanden.");
                return;
            }
            hsNumber = reader.GetInt64(0);
            hsUser = reader.GetInt64(1);
            hsTimestamp = reader.GetInt64(2);
        }

        var embed = new DiscordEmbedBuilder()
            .WithTitle($"Info für ``{ctx.Guild.Name}``")
            .WithDescription(
                $"Aktuelle Zahl: **{lastNumber}**\n" +
                $"Letzte Zahl von: <@{lastUser}>\n" +
                $"Highscore: ``{hsNumber}``, Erreicht von: <@{hsUser}>, Erreicht am: <t:{hsTimestamp}:R>")
            .WithColor(new DiscordColor(0x9B59B6))
            .Build();

        await ctx.RespondAsync(embed);
    }

    [Command("countinghighscore")]
    [Aliases("countinghigh")]
    [RequireDatabase]
    [Description("Zeigt den Counting Highscore an.")]
    public async Task CountingHighscore(CommandContext ctx)
    {
        var conn = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

        long hsNumber, hsUser, hsTimestamp;
        await using (var cmd = conn.CreateCommand("SELECT number, userid, timestamps FROM countinghighscore LIMIT 1"))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            if (!await reader.ReadAsync())
            {
                await ctx.RespondAsync("Kein Highscore vorhanden.");
                return;
            }
            hsNumber = reader.GetInt64(0);
            hsUser = reader.GetInt64(1);
            hsTimestamp = reader.GetInt64(2);
        }

        var embed = new DiscordEmbedBuilder()
            .WithTitle($"Highscore für ``{ctx.Guild.Name}``")
            .WithDescription($"Highscore: ``{hsNumber}``, Erreicht von: <@{hsUser}>, Erreicht am: <t:{hsTimestamp}:R>")
            .WithColor(new DiscordColor(0x9B59B6))
            .Build();

        await ctx.RespondAsync(embed);
    }

    [Command("countingprofile")]
    [RequireDatabase]
    [Description("Zeigt das Counting-Profil eines Users an.")]
    public async Task CountingProfile(CommandContext ctx, DiscordMember user = null)
    {
        user ??= ctx.Member;
        long userId = (long)user.Id;
        var conn = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

        long counts = 0, timestamps = 0;
        await using (var cmd = conn.CreateCommand("SELECT counter, timestamps FROM countcounter WHERE userid = @uid"))
        {
            cmd.Parameters.AddWithValue("uid", userId);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                await ctx.RespondAsync("Keine Zählung vorhanden.");
                return;
            }
            counts = reader.GetInt64(0);
            timestamps = reader.IsDBNull(1) ? 0 : reader.GetInt64(1);
        }

        string ts = timestamps > 0 ? $"<t:{timestamps}:R>" : "``N.A.``";

        decimal saves = 0m;
        await using (var cmd = conn.CreateCommand("SELECT saves FROM countsave WHERE userid = @uid"))
        {
            cmd.Parameters.AddWithValue("uid", userId);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync() && !reader.IsDBNull(0))
                saves = reader.GetDecimal(0);
        }

        long fails = 0;
        await using (var cmd = conn.CreateCommand("SELECT counter FROM countingfails WHERE userid = @uid"))
        {
            cmd.Parameters.AddWithValue("uid", userId);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
                fails = reader.GetInt64(0);
        }

        var embed = new DiscordEmbedBuilder()
            .WithTitle($"{user.DisplayName}'s Zählprofil")
            .WithDescription(
                $"Du hast ``{counts}`` mal gezählt.\n" +
                $"Zuletzt gezählt am: {ts}\n" +
                $"Du hast ``{fails}`` Fehlversuche gemacht.\n" +
                $"Du hast ``{saves}`` Sicherungen.")
            .WithColor(new DiscordColor(0x9B59B6))
            .WithFooter($"{ctx.User.Username}", ctx.User.AvatarUrl)
            .Build();

        await ctx.RespondAsync(embed);
    }

    [Command("countingtop")]
    [RequireDatabase]
    [Description("Zeigt die Top 15 Zähler an.")]
    public async Task CountingTop(CommandContext ctx)
    {
        var conn = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

        var embedBuilder = new DiscordEmbedBuilder()
            .WithTitle($"Top 15 für ``{ctx.Guild.Name}``")
            .WithColor(new DiscordColor(0x9B59B6));

        await using var cmd = conn.CreateCommand("SELECT userid, counter FROM countcounter ORDER BY counter DESC LIMIT 15");
        await using var reader = await cmd.ExecuteReaderAsync();

        int rank = 1;
        while (await reader.ReadAsync())
        {
            long uid = reader.GetInt64(0);
            long count = reader.GetInt64(1);
            embedBuilder.AddField(new DiscordEmbedField($"{rank}. <@{uid}>", $"{count}"));
            rank++;
        }

        if (rank == 1)
        {
            await ctx.RespondAsync("Noch keine Zählungen vorhanden.");
            return;
        }

        await ctx.RespondAsync(embedBuilder.Build());
    }

    [Command("givesaves")]
    [RequireStaffRole]
    [RequireDatabase]
    [Description("Gibt einem User Sicherungen (Staff only).")]
    public async Task GiveSaves(CommandContext ctx, DiscordMember user, decimal amount)
    {
        long userId = (long)user.Id;
        var conn = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

        bool exists = false;
        await using (var checkCmd = conn.CreateCommand("SELECT 1 FROM countsave WHERE userid = @uid"))
        {
            checkCmd.Parameters.AddWithValue("uid", userId);
            await using var reader = await checkCmd.ExecuteReaderAsync();
            exists = await reader.ReadAsync();
        }

        if (!exists)
        {
            await using var insertCmd = conn.CreateCommand("INSERT INTO countsave (userid, saves) VALUES (@uid, @saves)");
            insertCmd.Parameters.AddWithValue("uid", userId);
            insertCmd.Parameters.AddWithValue("saves", amount);
            await insertCmd.ExecuteNonQueryAsync();
        }
        else
        {
            await using var updateCmd = conn.CreateCommand("UPDATE countsave SET saves = saves + @amount WHERE userid = @uid");
            updateCmd.Parameters.AddWithValue("amount", amount);
            updateCmd.Parameters.AddWithValue("uid", userId);
            await updateCmd.ExecuteNonQueryAsync();
        }

        await ctx.RespondAsync($"✅ {user.Mention} hat **{amount}** Sicherung(en) erhalten.");
    }
}
