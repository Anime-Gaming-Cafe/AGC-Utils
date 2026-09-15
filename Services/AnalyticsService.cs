namespace AGC_Management.Services;

public readonly record struct WeeklyCount(DateOnly WeekStart, long Count);

public readonly record struct ChannelCount(ulong ChannelId, long Count);

public readonly record struct ModeratorCaseCount(ulong PunisherId, long WarnCount, long FlagCount, long BanCount);

public readonly record struct CaseTypeTotals(long WarnCount, long FlagCount, long BanCount);

public readonly record struct TicketTypeStats(string TicketType, long ClosedCount, double AvgHours, double MedianHours);

public static class AnalyticsService
{
    public static Task<List<WeeklyCount>> GetWeeklyMessageCounts(int weeks = 12) =>
        GetWeeklyCountsAsync("metrics_messages", "timestamp", weeks);

    public static Task<List<WeeklyCount>> GetWeeklyVoicePresenceCounts(int weeks = 12) =>
        GetWeeklyCountsAsync("metrics_voice", "timestamp", weeks);

    public static Task<List<WeeklyCount>> GetWeeklyTicketsOpened(int weeks = 12) =>
        GetWeeklyCountsAsync("ticketstore", "opened_at", weeks);

    public static async Task<List<WeeklyCount>> GetWeeklyCaseCounts(int weeks = 12)
    {
        var warns = await GetWeeklyCountsAsync("warns", "datum", weeks);
        var flags = await GetWeeklyCountsAsync("flags", "datum", weeks);
        var bans = await GetWeeklyCountsAsync("bans", "datum", weeks);

        Dictionary<DateOnly, long> merged = [];
        foreach (var w in warns) merged[w.WeekStart] = merged.GetValueOrDefault(w.WeekStart) + w.Count;
        foreach (var f in flags) merged[f.WeekStart] = merged.GetValueOrDefault(f.WeekStart) + f.Count;
        foreach (var b in bans) merged[b.WeekStart] = merged.GetValueOrDefault(b.WeekStart) + b.Count;

        return FillWeeklyGaps(merged, weeks);
    }

    public static async Task<List<ChannelCount>> GetTopChannelsByMessages(int weeks = 12, int limit = 10)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-weeks * 7).ToUnixTimeSeconds();
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = con.CreateCommand(
            "SELECT channelid, COUNT(*) FROM metrics_messages WHERE timestamp >= @since " +
            "GROUP BY channelid ORDER BY COUNT(*) DESC LIMIT @limit");
        cmd.Parameters.AddWithValue("since", since);
        cmd.Parameters.AddWithValue("limit", limit);

        List<ChannelCount> results = [];
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            results.Add(new ChannelCount((ulong)reader.GetInt64(0), reader.GetInt64(1)));

        return results;
    }

    public static async Task<List<ModeratorCaseCount>> GetCaseCountsByModerator(int weeks = 12)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-weeks * 7).ToUnixTimeSeconds();
        var warns = await GetCaseCountsByPunisherAsync("warns", since);
        var flags = await GetCaseCountsByPunisherAsync("flags", since);
        var bans = await GetCaseCountsByPunisherAsync("bans", since);

        var punisherIds = warns.Keys.Union(flags.Keys).Union(bans.Keys);

        return punisherIds
            .Select(id => new ModeratorCaseCount(id, warns.GetValueOrDefault(id), flags.GetValueOrDefault(id),
                bans.GetValueOrDefault(id)))
            .OrderByDescending(m => m.WarnCount + m.FlagCount + m.BanCount)
            .ToList();
    }

    public static async Task<CaseTypeTotals> GetCaseTypeTotals(int weeks = 12)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-weeks * 7).ToUnixTimeSeconds();
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

        async Task<long> CountSince(string table)
        {
            await using var cmd = con.CreateCommand($"SELECT COUNT(*) FROM {table} WHERE datum >= @since");
            cmd.Parameters.AddWithValue("since", since);
            return (long)(await cmd.ExecuteScalarAsync() ?? 0L);
        }

        return new CaseTypeTotals(await CountSince("warns"), await CountSince("flags"), await CountSince("bans"));
    }

    public static async Task<List<TicketTypeStats>> GetTicketResolutionStats(int weeks = 12)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-weeks * 7).ToUnixTimeSeconds();
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = con.CreateCommand(
            "SELECT tickettype, COUNT(*), " +
            "AVG(closed_at - opened_at)::double precision AS avg_seconds, " +
            "PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY closed_at - opened_at) AS median_seconds " +
            "FROM ticketstore " +
            "WHERE closed = true AND opened_at > 0 AND closed_at > 0 AND closed_at >= @since " +
            "GROUP BY tickettype ORDER BY tickettype");
        cmd.Parameters.AddWithValue("since", since);

        List<TicketTypeStats> results = [];
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            results.Add(new TicketTypeStats(
                reader.GetString(0),
                reader.GetInt64(1),
                reader.GetDouble(2) / 3600.0,
                reader.GetDouble(3) / 3600.0));

        return results;
    }

    private static async Task<List<WeeklyCount>> GetWeeklyCountsAsync(string table, string timestampColumn,
        int weeks)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-weeks * 7).ToUnixTimeSeconds();
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = con.CreateCommand(
            $"SELECT date_trunc('week', to_timestamp({timestampColumn})) AS week_start, COUNT(*) " +
            $"FROM {table} WHERE {timestampColumn} >= @since GROUP BY week_start ORDER BY week_start");
        cmd.Parameters.AddWithValue("since", since);

        Dictionary<DateOnly, long> raw = [];
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var weekStart = DateOnly.FromDateTime(reader.GetFieldValue<DateTimeOffset>(0).UtcDateTime);
            raw[weekStart] = reader.GetInt64(1);
        }

        return FillWeeklyGaps(raw, weeks);
    }

    private static async Task<Dictionary<ulong, long>> GetCaseCountsByPunisherAsync(string table, long since)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = con.CreateCommand(
            $"SELECT punisherid, COUNT(*) FROM {table} WHERE datum >= @since GROUP BY punisherid");
        cmd.Parameters.AddWithValue("since", since);

        Dictionary<ulong, long> result = [];
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result[(ulong)reader.GetInt64(0)] = reader.GetInt64(1);

        return result;
    }

    private static List<WeeklyCount> FillWeeklyGaps(Dictionary<DateOnly, long> raw, int weeks)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var daysSinceMonday = ((int)today.DayOfWeek + 6) % 7;
        var thisWeekStart = today.AddDays(-daysSinceMonday);

        List<WeeklyCount> result = [];
        for (var i = weeks - 1; i >= 0; i--)
        {
            var weekStart = thisWeekStart.AddDays(-7 * i);
            result.Add(new WeeklyCount(weekStart, raw.GetValueOrDefault(weekStart)));
        }

        return result;
    }
}
