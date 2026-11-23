#region

using AGC_Management.Entities.Entbannung;
using AGC_Management.Utils;

#endregion

namespace AGC_Management.Services;

public static class EntbannungService
{
    private static NpgsqlDataSource Db => CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

    public static async Task<DiscordGuild> GetUnbanGuildAsync(DiscordClient client)
    {
        var id = EntbannungConfig.ServerId;
        return client.Guilds.TryGetValue(id, out var cached) ? cached : await client.GetGuildAsync(id);
    }

    public static string GetTeamPing()
    {
        return $"<@&{EntbannungConfig.PingRoleId}>";
    }

    public static string GenerateCaseId()
    {
        return Guid.NewGuid().ToString("N")[..10];
    }

    public static string BoolToEmoji(bool value)
    {
        return value ? "<:angenommen:1190335045341282314>" : "<:abgelehnt:1190335046591205426>";
    }

    public static string VoteId(ulong voteMessageId)
    {
        return ((long)EntbannungConfig.AbstimmungsChannelId + (long)voteMessageId).ToString();
    }

    public static async Task<ulong?> GetTicketUserIdAsync(DiscordChannel channel)
    {
        try
        {
            var messages = await channel.GetMessagesAfterAsync(channel.Id, 1);
            return messages?.FirstOrDefault()?.MentionedUsers?.FirstOrDefault()?.Id;
        }
        catch
        {
            return null;
        }
    }

    #region Antragssperre

    public static async Task<Antragssperre?> GetSperreAsync(ulong userId)
    {
        await using var cmd = Db.CreateCommand(
            "SELECT reason, expires_at FROM antragssperre WHERE user_id = @user_id LIMIT 1");
        cmd.Parameters.AddWithValue("user_id", (long)userId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new Antragssperre(userId, reader.IsDBNull(0) ? "" : reader.GetString(0), reader.GetInt64(1));
    }

    public static async Task AddSperreAsync(ulong userId, string reason, long expiresAt)
    {
        await using var cmd = Db.CreateCommand(
            "INSERT INTO antragssperre (user_id, reason, expires_at) VALUES (@user_id, @reason, @expires_at)");
        cmd.Parameters.AddWithValue("user_id", (long)userId);
        cmd.Parameters.AddWithValue("reason", reason);
        cmd.Parameters.AddWithValue("expires_at", expiresAt);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task ExpireSperreAsync(ulong userId)
    {
        await using var cmd = Db.CreateCommand("UPDATE antragssperre SET expires_at = 0 WHERE user_id = @user_id");
        cmd.Parameters.AddWithValue("user_id", (long)userId);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task<List<ulong>> GetExpiredSperrenAsync(long now)
    {
        await using var cmd = Db.CreateCommand("SELECT user_id FROM antragssperre WHERE expires_at < @now");
        cmd.Parameters.AddWithValue("now", now);
        await using var reader = await cmd.ExecuteReaderAsync();
        List<ulong> result = [];
        while (await reader.ReadAsync()) result.Add((ulong)reader.GetInt64(0));
        return result;
    }

    public static async Task DeleteSperrenAsync(IEnumerable<ulong> userIds)
    {
        await using var cmd = Db.CreateCommand("DELETE FROM antragssperre WHERE user_id = ANY(@ids)");
        cmd.Parameters.AddWithValue("ids", userIds.Select(x => (long)x).ToArray());
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task<int> CountActiveBlocksAsync(long now)
    {
        await using var cmd = Db.CreateCommand(
            "SELECT (SELECT COUNT(*) FROM antragssperre WHERE expires_at = 0 OR expires_at > @now) + (SELECT COUNT(*) FROM permas)");
        cmd.Parameters.AddWithValue("now", now);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    #endregion

    #region PermaBlock

    public static async Task<bool> IsPermaBlockedAsync(ulong userId)
    {
        await using var cmd = Db.CreateCommand("SELECT EXISTS (SELECT 1 FROM permas WHERE userid = @userid)");
        cmd.Parameters.AddWithValue("userid", (long)userId);
        return (bool)(await cmd.ExecuteScalarAsync())!;
    }

    public static async Task<bool> AddPermaBlockAsync(ulong userId)
    {
        await using var cmd = Db.CreateCommand(
            "INSERT INTO permas (userid) SELECT @userid WHERE NOT EXISTS (SELECT 1 FROM permas WHERE userid = @userid)");
        cmd.Parameters.AddWithValue("userid", (long)userId);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    #endregion

    #region Antragsverlauf

    public static async Task<bool> AntragExistsAsync(string antragsId, ulong? userId = null)
    {
        var sql = userId is null
            ? "SELECT EXISTS (SELECT 1 FROM antragsverlauf WHERE antrags_id = @antrags_id)"
            : "SELECT EXISTS (SELECT 1 FROM antragsverlauf WHERE antrags_id = @antrags_id AND user_id = @user_id)";
        await using var cmd = Db.CreateCommand(sql);
        cmd.Parameters.AddWithValue("antrags_id", antragsId);
        if (userId is not null) cmd.Parameters.AddWithValue("user_id", (long)userId.Value);
        return (bool)(await cmd.ExecuteScalarAsync())!;
    }

    public static async Task AddAntragAsync(AntragsverlaufEntry entry)
    {
        await using var cmd = Db.CreateCommand(
            "INSERT INTO antragsverlauf (user_id, antrags_id, entbannt, reason, mod_id, timestamp) " +
            "VALUES (@user_id, @antrags_id, @entbannt, @reason, @mod_id, @timestamp)");
        cmd.Parameters.AddWithValue("user_id", (long)entry.UserId);
        cmd.Parameters.AddWithValue("antrags_id", entry.AntragsId);
        cmd.Parameters.AddWithValue("entbannt", entry.Entbannt);
        cmd.Parameters.AddWithValue("reason", entry.Reason);
        cmd.Parameters.AddWithValue("mod_id", (long)entry.ModId);
        cmd.Parameters.AddWithValue("timestamp", entry.Timestamp);
        await cmd.ExecuteNonQueryAsync();
    }

    public static Task<List<AntragsverlaufEntry>> GetAntragshistorieAsync(ulong userId)
    {
        return QueryAntraegeAsync(
            "SELECT user_id, antrags_id, entbannt, reason, mod_id, timestamp FROM antragsverlauf WHERE user_id = @user_id",
            cmd => cmd.Parameters.AddWithValue("user_id", (long)userId));
    }

    public static Task<List<AntragsverlaufEntry>> GetRecentAntraegeAsync(int count)
    {
        return QueryAntraegeAsync(
            "SELECT user_id, antrags_id, entbannt, reason, mod_id, timestamp FROM antragsverlauf ORDER BY timestamp DESC LIMIT @count",
            cmd => cmd.Parameters.AddWithValue("count", count));
    }

    private static async Task<List<AntragsverlaufEntry>> QueryAntraegeAsync(string sql, Action<NpgsqlCommand> bind)
    {
        await using var cmd = Db.CreateCommand(sql);
        bind(cmd);
        await using var reader = await cmd.ExecuteReaderAsync();
        List<AntragsverlaufEntry> result = [];
        while (await reader.ReadAsync())
            result.Add(new AntragsverlaufEntry(
                (ulong)reader.GetInt64(0),
                reader.GetString(1),
                reader.GetBoolean(2),
                reader.IsDBNull(3) ? "" : reader.GetString(3),
                (ulong)reader.GetInt64(4),
                reader.GetInt64(5)));
        return result;
    }

    public static async Task PostHistoryEmbedAsync(DiscordClient client, bool entbannt, DiscordUser modUser,
        string antragsnummer, DiscordUser targetUser, string grund)
    {
        var eb = new DiscordEmbedBuilder()
            .WithTitle(entbannt ? "Antrag wurde angenommen!" : "Antrag wurde abgelehnt!")
            .WithColor(entbannt ? DiscordColor.Green : DiscordColor.Red)
            .WithDescription(
                $"**Status:** {BoolToEmoji(entbannt)}\n**Bearbeitet von:** {modUser.Mention} ({modUser.Id}) \n**Antragsnummer:** {antragsnummer}\n**Betroffener User:** {targetUser.Mention} ({targetUser.Id})\n**Grund:** {grund}")
            .WithFooter("Entbannungssystem", modUser.AvatarUrl)
            .WithTimestamp(DateTimeOffset.Now);

        var ch = await client.GetChannelAsync(EntbannungConfig.HistoryChannelId);
        await ch.SendMessageAsync(eb);
    }

    public static async Task<bool> TryAddAntragsverlaufAsync(DiscordClient client, bool entbannt,
        DiscordUser modUser, string antragsnummer, DiscordUser targetUser, string grund)
    {
        try
        {
            if (await AntragExistsAsync(antragsnummer, targetUser.Id)) return false;

            await AddAntragAsync(new AntragsverlaufEntry(targetUser.Id, antragsnummer, entbannt, grund, modUser.Id,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
            await PostHistoryEmbedAsync(client, entbannt, modUser, antragsnummer, targetUser, grund);
            return true;
        }
        catch (Exception ex)
        {
            await ErrorReporting.SendErrorToDev(client, client.CurrentUser, ex);
            return false;
        }
    }

    #endregion

    #region Abstimmungen

    private const string AbstimmungColumns =
        "channel_id, message_id, expires_at, created_by, pvotes, nvotes, endpending";

    private static Abstimmung ReadAbstimmung(NpgsqlDataReader reader)
    {
        return new Abstimmung(
            (ulong)reader.GetInt64(0),
            (ulong)reader.GetInt64(1),
            reader.GetInt64(2),
            reader.IsDBNull(3) ? 0 : (ulong)reader.GetInt64(3),
            Convert.ToInt32(reader.GetValue(4)),
            Convert.ToInt32(reader.GetValue(5)),
            !reader.IsDBNull(6) && reader.GetBoolean(6));
    }

    private static async Task<List<Abstimmung>> QueryAbstimmungenAsync(string where, Action<NpgsqlCommand> bind)
    {
        await using var cmd = Db.CreateCommand($"SELECT {AbstimmungColumns} FROM abstimmungen WHERE {where}");
        bind(cmd);
        await using var reader = await cmd.ExecuteReaderAsync();
        List<Abstimmung> result = [];
        while (await reader.ReadAsync()) result.Add(ReadAbstimmung(reader));
        return result;
    }

    public static async Task<Abstimmung?> GetAbstimmungByChannelAsync(ulong antragChannelId)
    {
        return (await QueryAbstimmungenAsync("channel_id = @id",
            cmd => cmd.Parameters.AddWithValue("id", (long)antragChannelId))).FirstOrDefault();
    }

    public static async Task<Abstimmung?> GetAbstimmungByMessageAsync(ulong messageId)
    {
        return (await QueryAbstimmungenAsync("message_id = @id",
            cmd => cmd.Parameters.AddWithValue("id", (long)messageId))).FirstOrDefault();
    }

    public static Task<List<Abstimmung>> GetActiveAbstimmungenAsync(long now)
    {
        return QueryAbstimmungenAsync("expires_at > @now", cmd => cmd.Parameters.AddWithValue("now", now));
    }

    public static Task<List<Abstimmung>> GetFinishedAbstimmungenAsync(long now)
    {
        return QueryAbstimmungenAsync("expires_at < @now OR endpending = true",
            cmd => cmd.Parameters.AddWithValue("now", now));
    }

    public static async Task CreateAbstimmungAsync(ulong antragChannelId, ulong messageId, long expiresAt,
        ulong createdBy)
    {
        await using var cmd = Db.CreateCommand(
            "INSERT INTO abstimmungen (channel_id, message_id, expires_at, created_by, pvotes, nvotes, endpending) " +
            "VALUES (@channel_id, @message_id, @expires_at, @created_by, 0, 0, false)");
        cmd.Parameters.AddWithValue("channel_id", (long)antragChannelId);
        cmd.Parameters.AddWithValue("message_id", (long)messageId);
        cmd.Parameters.AddWithValue("expires_at", expiresAt);
        cmd.Parameters.AddWithValue("created_by", (long)createdBy);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task DeleteAbstimmungAsync(Abstimmung abstimmung)
    {
        await using var batch = Db.CreateBatch();
        var deleteVotes = new NpgsqlBatchCommand("DELETE FROM abstimmungen_teamler WHERE vote_id = @vote_id");
        deleteVotes.Parameters.AddWithValue("vote_id", VoteId(abstimmung.MessageId));
        var deleteVote = new NpgsqlBatchCommand("DELETE FROM abstimmungen WHERE channel_id = @channel_id");
        deleteVote.Parameters.AddWithValue("channel_id", (long)abstimmung.ChannelId);
        batch.BatchCommands.Add(deleteVotes);
        batch.BatchCommands.Add(deleteVote);
        await batch.ExecuteNonQueryAsync();
    }

    public static async Task<VoteType?> GetUserVoteAsync(ulong voteMessageId, ulong userId)
    {
        await using var cmd = Db.CreateCommand(
            "SELECT votevalue FROM abstimmungen_teamler WHERE vote_id = @vote_id AND user_id = @user_id LIMIT 1");
        cmd.Parameters.AddWithValue("vote_id", VoteId(voteMessageId));
        cmd.Parameters.AddWithValue("user_id", (long)userId);
        var result = await cmd.ExecuteScalarAsync();
        if (result is null or DBNull) return null;
        return Convert.ToInt32(result) == 1 ? VoteType.Positive : VoteType.Negative;
    }

    public static async Task<List<(ulong UserId, VoteType Vote)>> GetVotesAsync(ulong voteMessageId)
    {
        await using var cmd = Db.CreateCommand(
            "SELECT user_id, votevalue FROM abstimmungen_teamler WHERE vote_id = @vote_id");
        cmd.Parameters.AddWithValue("vote_id", VoteId(voteMessageId));
        await using var reader = await cmd.ExecuteReaderAsync();
        List<(ulong, VoteType)> result = [];
        while (await reader.ReadAsync())
            result.Add(((ulong)reader.GetInt64(0), Convert.ToInt32(reader.GetValue(1)) == 1 ? VoteType.Positive : VoteType.Negative));
        return result;
    }

    public static async Task AddVoteAsync(DiscordClient client, ulong voteMessageId, ulong userId, bool positive)
    {
        var column = positive ? "pvotes" : "nvotes";
        await using var batch = Db.CreateBatch();
        var insert = new NpgsqlBatchCommand(
            "INSERT INTO abstimmungen_teamler (user_id, vote_id, votevalue) VALUES (@user_id, @vote_id, @value)");
        insert.Parameters.AddWithValue("user_id", (long)userId);
        insert.Parameters.AddWithValue("vote_id", VoteId(voteMessageId));
        insert.Parameters.AddWithValue("value", positive ? 1 : 0);
        var update = new NpgsqlBatchCommand(
            $"UPDATE abstimmungen SET {column} = {column} + 1 WHERE message_id = @message_id");
        update.Parameters.AddWithValue("message_id", (long)voteMessageId);
        batch.BatchCommands.Add(insert);
        batch.BatchCommands.Add(update);
        await batch.ExecuteNonQueryAsync();

        await UpdateEndPendingAsync(client, voteMessageId);
    }

    public static async Task RemoveVoteAsync(DiscordClient client, ulong voteMessageId, ulong userId, VoteType vote)
    {
        var column = vote == VoteType.Positive ? "pvotes" : "nvotes";
        await using var batch = Db.CreateBatch();
        var delete = new NpgsqlBatchCommand(
            "DELETE FROM abstimmungen_teamler WHERE vote_id = @vote_id AND user_id = @user_id");
        delete.Parameters.AddWithValue("vote_id", VoteId(voteMessageId));
        delete.Parameters.AddWithValue("user_id", (long)userId);
        var update = new NpgsqlBatchCommand(
            $"UPDATE abstimmungen SET {column} = GREATEST({column} - 1, 0) WHERE message_id = @message_id");
        update.Parameters.AddWithValue("message_id", (long)voteMessageId);
        batch.BatchCommands.Add(delete);
        batch.BatchCommands.Add(update);
        await batch.ExecuteNonQueryAsync();

        await UpdateEndPendingAsync(client, voteMessageId);
    }

    public static int GetVoteColor(int pvotes, int nvotes)
    {
        if (pvotes == 0 && nvotes == 0) return -1;
        if (pvotes == nvotes) return 2;
        return pvotes > nvotes ? 1 : 0;
    }

    public static async Task<int> GetTeamMemberCountAsync(DiscordClient client)
    {
        try
        {
            var unbanGuild = await GetUnbanGuildAsync(client);
            var pingRoleId = EntbannungConfig.PingRoleId;
            return unbanGuild.Members.Values.Count(m => m.Roles.Any(r => r.Id == pingRoleId));
        }
        catch
        {
            return 0;
        }
    }

    private static async Task UpdateEndPendingAsync(DiscordClient client, ulong voteMessageId)
    {
        try
        {
            var abstimmung = await GetAbstimmungByMessageAsync(voteMessageId);
            if (abstimmung is null) return;

            var pvotes = abstimmung.PositiveVotes;
            var nvotes = abstimmung.NegativeVotes;
            var teamMemberCount = await GetTeamMemberCountAsync(client);

            var endPending = false;
            if (teamMemberCount > 0 && pvotes != nvotes)
            {
                var thresholdReached = (double)(pvotes + nvotes) / teamMemberCount * 100 > 70;
                var remaining = teamMemberCount - (pvotes + nvotes);
                var outcomeDecided = pvotes > teamMemberCount / 2 || nvotes > teamMemberCount / 2 ||
                                     pvotes + remaining < nvotes || nvotes + remaining < pvotes;
                endPending = thresholdReached || outcomeDecided;
            }

            await using var cmd = Db.CreateCommand(
                "UPDATE abstimmungen SET endpending = @endpending WHERE message_id = @message_id");
            cmd.Parameters.AddWithValue("endpending", endPending);
            cmd.Parameters.AddWithValue("message_id", (long)voteMessageId);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception e)
        {
            CurrentApplication.Logger.Error(e, "Entbannung: EndPending-Update fehlgeschlagen");
        }
    }

    #endregion

    #region Automations

    public static async Task<bool> IsAutomationDisabledAsync(ulong channelId)
    {
        await using var cmd = Db.CreateCommand(
            "SELECT EXISTS (SELECT 1 FROM disabled_automations WHERE channel_id = @channel_id)");
        cmd.Parameters.AddWithValue("channel_id", (long)channelId);
        return (bool)(await cmd.ExecuteScalarAsync())!;
    }

    public static async Task<bool> ToggleAutomationAsync(ulong channelId, ulong userId)
    {
        await using var delete = Db.CreateCommand("DELETE FROM disabled_automations WHERE channel_id = @channel_id");
        delete.Parameters.AddWithValue("channel_id", (long)channelId);
        if (await delete.ExecuteNonQueryAsync() > 0) return false;

        await using var insert = Db.CreateCommand(
            "INSERT INTO disabled_automations (channel_id, disabled_by, timestamp) VALUES (@channel_id, @disabled_by, @timestamp)");
        insert.Parameters.AddWithValue("channel_id", (long)channelId);
        insert.Parameters.AddWithValue("disabled_by", (long)userId);
        insert.Parameters.AddWithValue("timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        await insert.ExecuteNonQueryAsync();
        return true;
    }

    #endregion

    public static async Task<List<string>> GetAutocompletionsAsync(string type)
    {
        await using var cmd = Db.CreateCommand("SELECT data FROM autocompletions WHERE type = @type");
        cmd.Parameters.AddWithValue("type", type);
        await using var reader = await cmd.ExecuteReaderAsync();
        List<string> result = [];
        while (await reader.ReadAsync()) result.Add(reader.GetString(0));
        return result;
    }

    public static async Task AddFlagAsync(ulong userId, ulong punisherId, string description, string caseId)
    {
        await using var cmd = Db.CreateCommand(
            "INSERT INTO flags (userid, punisherid, datum, description, caseid) VALUES (@userid, @punisherid, @datum, @description, @caseid)");
        cmd.Parameters.AddWithValue("userid", (long)userId);
        cmd.Parameters.AddWithValue("punisherid", (long)punisherId);
        cmd.Parameters.AddWithValue("datum", DateTimeOffset.Now.ToUnixTimeSeconds());
        cmd.Parameters.AddWithValue("description", description);
        cmd.Parameters.AddWithValue("caseid", caseId);
        await cmd.ExecuteNonQueryAsync();
    }
}
