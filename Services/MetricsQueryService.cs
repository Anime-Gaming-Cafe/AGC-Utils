#region

using System.Text;
using AGC_Management.Entities.ActivityRoles;
using AGC_Management.Enums.ActivityRoles;

#endregion

namespace AGC_Management.Services;

/// <summary>
///     Shared windowed-counting queries over the metrics_* row logs (metrics_messages, metrics_voice).
///     Deliberately independent of any one feature - Extra-Permissions, Activity Roles and a future
///     Giveaway system all need "how active was this user in this window/scope", just with different
///     framing (threshold vs. ranking), so the SQL lives here once instead of per feature.
/// </summary>
public static class MetricsQueryService
{
    public static string MetricTableFor(ActivityRoleMetric metric)
    {
        return metric == ActivityRoleMetric.Messages ? "metrics_messages" : "metrics_voice";
    }

    /// <summary>
    ///     Category ids are stored, not their children, so a channel moved into the category starts
    ///     counting without touching the rule/condition that references it.
    /// </summary>
    public static HashSet<ulong> ExpandScope(DiscordGuild? guild, long[] scopeIds)
    {
        var expanded = new HashSet<ulong>();
        foreach (var raw in scopeIds)
        {
            var id = (ulong)raw;
            if (guild != null && guild.Channels.TryGetValue(id, out var channel) &&
                channel.Type == ChannelType.Category)
            {
                foreach (var child in guild.Channels.Values.Where(c => c.ParentId == id)) expanded.Add(child.Id);
                continue;
            }

            expanded.Add(id);
        }

        return expanded;
    }

    private static void AppendScopeFilters(StringBuilder sql, HashSet<ulong> include, HashSet<ulong> exclude)
    {
        if (include.Count > 0) sql.Append(" AND channelid = ANY(@include)");
        if (exclude.Count > 0) sql.Append(" AND channelid != ALL(@exclude)");
    }

    private static void AddScopeParameters(NpgsqlCommand cmd, HashSet<ulong> include, HashSet<ulong> exclude)
    {
        if (include.Count > 0) cmd.Parameters.AddWithValue("include", include.Select(x => (long)x).ToArray());
        if (exclude.Count > 0) cmd.Parameters.AddWithValue("exclude", exclude.Select(x => (long)x).ToArray());
    }

    /// <summary>
    ///     Only metrics_voice rows carry a voicestate, so this is a no-op for metrics_messages - a caller
    ///     computing a Combined score can pass the same exclusion list into both halves safely.
    /// </summary>
    private static void AppendVoiceStateFilter(StringBuilder sql, string table, int[]? excludeVoiceStates)
    {
        if (table == "metrics_voice" && excludeVoiceStates is { Length: > 0 })
            sql.Append(" AND voicestate != ALL(@excludeVoiceStates)");
    }

    private static void AddVoiceStateParameter(NpgsqlCommand cmd, string table, int[]? excludeVoiceStates)
    {
        if (table == "metrics_voice" && excludeVoiceStates is { Length: > 0 })
            cmd.Parameters.AddWithValue("excludeVoiceStates", excludeVoiceStates);
    }

    /// <summary>
    ///     Both metric tables are row logs, so a count is the metric: one row per message, and one row per
    ///     user and minute for voice. sinceUnix &lt;= 0 counts everything ever recorded.
    /// </summary>
    public static async Task<long> CountMetricAsync(string table, ulong userId, long sinceUnix, long? untilUnix,
        long[] includeScope, long[] excludeScope, DiscordGuild? guild = null)
    {
        var include = ExpandScope(guild, includeScope);
        var exclude = ExpandScope(guild, excludeScope);

        var sql = new StringBuilder($"SELECT COUNT(*) FROM {table} WHERE userid = @userid");
        if (sinceUnix > 0) sql.Append(" AND timestamp >= @since");
        if (untilUnix.HasValue) sql.Append(" AND timestamp < @until");
        AppendScopeFilters(sql, include, exclude);

        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = con.CreateCommand(sql.ToString());
        cmd.Parameters.AddWithValue("userid", (long)userId);
        if (sinceUnix > 0) cmd.Parameters.AddWithValue("since", sinceUnix);
        if (untilUnix.HasValue) cmd.Parameters.AddWithValue("until", untilUnix.Value);
        AddScopeParameters(cmd, include, exclude);

        var result = await cmd.ExecuteScalarAsync();
        return result is long count ? count : 0;
    }

    /// <summary>
    ///     Users ranked by activity in the window, highest first, limited to the top <paramref name="limit" />
    ///     candidates so a caller can drop ineligible ones and still have enough left to fill its ranks.
    /// </summary>
    public static async Task<List<ActivityRoleCandidate>> GetRankedUsersAsync(string table, long sinceUnix,
        long? untilUnix, long[] includeScope, long[] excludeScope, long minCount, int limit,
        DiscordGuild? guild = null, int[]? excludeVoiceStates = null)
    {
        var include = ExpandScope(guild, includeScope);
        var exclude = ExpandScope(guild, excludeScope);

        var sql = new StringBuilder($"SELECT userid, COUNT(*) AS cnt FROM {table} WHERE true");
        if (sinceUnix > 0) sql.Append(" AND timestamp >= @since");
        if (untilUnix.HasValue) sql.Append(" AND timestamp < @until");
        AppendScopeFilters(sql, include, exclude);
        AppendVoiceStateFilter(sql, table, excludeVoiceStates);
        sql.Append(" GROUP BY userid HAVING COUNT(*) >= @minCount ORDER BY cnt DESC LIMIT @limit");

        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = con.CreateCommand(sql.ToString());
        if (sinceUnix > 0) cmd.Parameters.AddWithValue("since", sinceUnix);
        if (untilUnix.HasValue) cmd.Parameters.AddWithValue("until", untilUnix.Value);
        AddScopeParameters(cmd, include, exclude);
        AddVoiceStateParameter(cmd, table, excludeVoiceStates);
        cmd.Parameters.AddWithValue("minCount", minCount);
        cmd.Parameters.AddWithValue("limit", limit);

        var results = new List<ActivityRoleCandidate>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            results.Add(new ActivityRoleCandidate { UserId = (ulong)reader.GetInt64(0), Count = reader.GetInt64(1) });

        return results;
    }

    /// <summary>
    ///     Users whose activity in the window crosses a threshold. Only users with at least one matching
    ///     row can appear (COUNT is over existing rows), same limitation the old per-condition query had.
    /// </summary>
    public static async Task<List<ulong>> GetUsersOverThresholdAsync(string table, long sinceUnix, long? untilUnix,
        long[] includeScope, long[] excludeScope, bool lte, long value, DiscordGuild? guild = null,
        int[]? excludeVoiceStates = null)
    {
        var include = ExpandScope(guild, includeScope);
        var exclude = ExpandScope(guild, excludeScope);

        var sql = new StringBuilder($"SELECT userid FROM {table} WHERE true");
        if (sinceUnix > 0) sql.Append(" AND timestamp >= @since");
        if (untilUnix.HasValue) sql.Append(" AND timestamp < @until");
        AppendScopeFilters(sql, include, exclude);
        AppendVoiceStateFilter(sql, table, excludeVoiceStates);
        sql.Append(lte ? " GROUP BY userid HAVING COUNT(*) <= @value" : " GROUP BY userid HAVING COUNT(*) >= @value");

        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = con.CreateCommand(sql.ToString());
        if (sinceUnix > 0) cmd.Parameters.AddWithValue("since", sinceUnix);
        if (untilUnix.HasValue) cmd.Parameters.AddWithValue("until", untilUnix.Value);
        AddScopeParameters(cmd, include, exclude);
        AddVoiceStateParameter(cmd, table, excludeVoiceStates);
        cmd.Parameters.AddWithValue("value", value);

        var userIds = new List<ulong>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) userIds.Add((ulong)reader.GetInt64(0));

        return userIds;
    }

    private static async Task<Dictionary<ulong, long>> GetPerUserCountsAsync(string table, long sinceUnix,
        long? untilUnix, long[] includeScope, long[] excludeScope, DiscordGuild? guild,
        int[]? excludeVoiceStates = null)
    {
        var include = ExpandScope(guild, includeScope);
        var exclude = ExpandScope(guild, excludeScope);

        var sql = new StringBuilder($"SELECT userid, COUNT(*) AS cnt FROM {table} WHERE true");
        if (sinceUnix > 0) sql.Append(" AND timestamp >= @since");
        if (untilUnix.HasValue) sql.Append(" AND timestamp < @until");
        AppendScopeFilters(sql, include, exclude);
        AppendVoiceStateFilter(sql, table, excludeVoiceStates);
        sql.Append(" GROUP BY userid");

        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = con.CreateCommand(sql.ToString());
        if (sinceUnix > 0) cmd.Parameters.AddWithValue("since", sinceUnix);
        if (untilUnix.HasValue) cmd.Parameters.AddWithValue("until", untilUnix.Value);
        AddScopeParameters(cmd, include, exclude);
        AddVoiceStateParameter(cmd, table, excludeVoiceStates);

        var counts = new Dictionary<ulong, long>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) counts[(ulong)reader.GetInt64(0)] = reader.GetInt64(1);

        return counts;
    }

    /// <summary>
    ///     Sums each user's message count and voice-minute count in the window into one score, for
    ///     <see cref="ActivityRoleMetric.Combined" />. Both queries share the same include/exclude scope,
    ///     which works because a text-channel id simply never matches a voice-log row and vice versa.
    /// </summary>
    private static async Task<Dictionary<ulong, long>> GetCombinedCountsAsync(long sinceUnix, long? untilUnix,
        long[] includeScope, long[] excludeScope, DiscordGuild? guild, int[]? excludeVoiceStates = null)
    {
        var messages = await GetPerUserCountsAsync("metrics_messages", sinceUnix, untilUnix, includeScope,
            excludeScope, guild);
        var voice = await GetPerUserCountsAsync("metrics_voice", sinceUnix, untilUnix, includeScope, excludeScope,
            guild, excludeVoiceStates);

        var combined = new Dictionary<ulong, long>(messages);
        foreach (var (userId, count) in voice)
            combined[userId] = combined.TryGetValue(userId, out var existing) ? existing + count : count;

        return combined;
    }

    public static async Task<List<ActivityRoleCandidate>> GetRankedUsersCombinedAsync(long sinceUnix,
        long? untilUnix, long[] includeScope, long[] excludeScope, long minCount, int limit,
        DiscordGuild? guild = null, int[]? excludeVoiceStates = null)
    {
        var combined = await GetCombinedCountsAsync(sinceUnix, untilUnix, includeScope, excludeScope, guild,
            excludeVoiceStates);
        return combined
            .Where(kv => kv.Value >= minCount)
            .OrderByDescending(kv => kv.Value)
            .Take(limit)
            .Select(kv => new ActivityRoleCandidate { UserId = kv.Key, Count = kv.Value })
            .ToList();
    }

    public static async Task<List<ulong>> GetUsersOverThresholdCombinedAsync(long sinceUnix, long? untilUnix,
        long[] includeScope, long[] excludeScope, bool lte, long value, DiscordGuild? guild = null,
        int[]? excludeVoiceStates = null)
    {
        var combined = await GetCombinedCountsAsync(sinceUnix, untilUnix, includeScope, excludeScope, guild,
            excludeVoiceStates);
        return combined
            .Where(kv => lte ? kv.Value <= value : kv.Value >= value)
            .Select(kv => kv.Key)
            .ToList();
    }
}
