#region

using System.Text.RegularExpressions;
using AGC_Management.Entities.ActivityRoles;
using AGC_Management.Enums.ActivityRoles;
using AGC_Management.Enums.Conditions;
using DisCatSharp.Exceptions;

#endregion

namespace AGC_Management.Services;

/// <summary>
///     Web-configurable "activity roles": Top-N leaderboard roles and threshold roles over the shared
///     metrics_* tables, with EligibilityService-backed exemptions. Mirrors ExtraPermissionService's
///     shape (cache, resolve, apply) but keeps its own grant bookkeeping because Top-N ranking is
///     exclusive across members in a way a single permission's Grant/Revoke/Untouched model does not
///     express.
/// </summary>
public static class ActivityRoleService
{
    public const string OwnerType = "activityrule";

    /// <summary>
    ///     The pieces a winner-line builder can arrange, in key/German-label form so the WebUI has one
    ///     place to source both the "add block" picker and existing-block labels from.
    /// </summary>
    public static readonly (string Key, string Label)[] WinnerLineBlockKeys =
    [
        ("medal", "Medaille / Rang-Badge"),
        ("mention", "Nutzer-Erwähnung"),
        ("count", "Zählwert"),
        ("role", "Rollen-Erwähnung")
    ];

    private static readonly TimeSpan RuleCacheTtl = TimeSpan.FromSeconds(30);
    private static List<ActivityRoleRule>? _ruleCache;
    private static DateTime _ruleCacheExpires = DateTime.MinValue;

    public readonly record struct RankedGrant(ulong UserId, ulong RoleId, int Rank, long Count);

    #region Rules

    public static async Task<List<ActivityRoleRule>> GetRulesAsync()
    {
        var cached = _ruleCache;
        if (cached != null && _ruleCacheExpires > DateTime.UtcNow) return cached;

        var rules = new List<ActivityRoleRule>();
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using (var cmd = con.CreateCommand(
                         "SELECT rule_id, name, enabled, metric, mode, window_type, window_days, window_start, window_end, " +
                         "scope_ids, exclude_scope_ids, min_activity, threshold_role_id, threshold_value, threshold_comparator, " +
                         "auto_revoke, announce_channel_id, announce_message, announce_interval_days, last_announced_at, " +
                         "winner_line_blocks, medal_rank1, medal_rank2, medal_rank3, medal_other_template, " +
                         "count_divisor, count_suffix, count_monospace, created_by, created_at " +
                         "FROM activity_role_rules ORDER BY name"))
        {
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) rules.Add(ReadRule(reader));
        }

        if (rules.Count > 0)
        {
            var byId = rules.ToDictionary(r => r.RuleId);
            foreach (var tier in await GetAllTiersAsync())
                if (byId.TryGetValue(tier.RuleId, out var rule))
                    rule.Tiers.Add(tier);
        }

        _ruleCache = rules;
        _ruleCacheExpires = DateTime.UtcNow + RuleCacheTtl;
        return rules;
    }

    private static void InvalidateCache()
    {
        _ruleCache = null;
    }

    public static async Task<ActivityRoleRule?> GetRuleAsync(string ruleId)
    {
        var rules = await GetRulesAsync();
        return rules.FirstOrDefault(r => r.RuleId == ruleId);
    }

    private static ActivityRoleRule ReadRule(NpgsqlDataReader reader)
    {
        return new ActivityRoleRule
        {
            RuleId = reader.GetString(0),
            Name = reader.IsDBNull(1) ? "" : reader.GetString(1),
            Enabled = !reader.IsDBNull(2) && reader.GetBoolean(2),
            Metric = ParseEnum(reader.IsDBNull(3) ? "messages" : reader.GetString(3), ActivityRoleMetric.Messages),
            Mode = ParseEnum(reader.IsDBNull(4) ? "topn" : reader.GetString(4), ActivityRoleMode.TopN),
            WindowType = ParseEnum(reader.IsDBNull(5) ? "rolling" : reader.GetString(5),
                ActivityRoleWindowType.Rolling),
            WindowDays = reader.IsDBNull(6) ? 7 : reader.GetInt32(6),
            WindowStart = reader.IsDBNull(7) ? null : reader.GetInt64(7),
            WindowEnd = reader.IsDBNull(8) ? null : reader.GetInt64(8),
            ScopeIds = reader.IsDBNull(9) ? [] : reader.GetFieldValue<long[]>(9),
            ExcludeScopeIds = reader.IsDBNull(10) ? [] : reader.GetFieldValue<long[]>(10),
            MinActivity = reader.IsDBNull(11) ? 0 : reader.GetInt64(11),
            ThresholdRoleId = reader.IsDBNull(12) ? 0 : (ulong)reader.GetInt64(12),
            ThresholdValue = reader.IsDBNull(13) ? 0 : reader.GetInt64(13),
            ThresholdComparator = ParseEnum(reader.IsDBNull(14) ? "gte" : reader.GetString(14),
                EligibilityComparator.Gte),
            AutoRevoke = !reader.IsDBNull(15) && reader.GetBoolean(15),
            AnnounceChannelId = reader.IsDBNull(16) ? 0 : (ulong)reader.GetInt64(16),
            AnnounceMessage = reader.IsDBNull(17) ? "" : reader.GetString(17),
            AnnounceIntervalDays = reader.IsDBNull(18) ? 0 : reader.GetInt32(18),
            LastAnnouncedAt = reader.IsDBNull(19) ? 0 : reader.GetInt64(19),
            WinnerLineBlocks = ParseWinnerLineBlocks(reader.IsDBNull(20) ? "" : reader.GetString(20)),
            MedalRank1 = reader.IsDBNull(21) ? "🥇" : reader.GetString(21),
            MedalRank2 = reader.IsDBNull(22) ? "🥈" : reader.GetString(22),
            MedalRank3 = reader.IsDBNull(23) ? "🥉" : reader.GetString(23),
            MedalOtherTemplate = reader.IsDBNull(24) ? "`#{rank}`" : reader.GetString(24),
            CountDivisor = reader.IsDBNull(25) ? 1 : reader.GetInt64(25),
            CountSuffix = reader.IsDBNull(26) ? "" : reader.GetString(26),
            CountMonospace = reader.IsDBNull(27) || reader.GetBoolean(27),
            CreatedBy = reader.IsDBNull(28) ? 0 : (ulong)reader.GetInt64(28),
            CreatedAt = reader.IsDBNull(29) ? 0 : reader.GetInt64(29)
        };
    }

    private static T ParseEnum<T>(string raw, T fallback) where T : struct, Enum
    {
        return Enum.TryParse<T>(raw, true, out var parsed) ? parsed : fallback;
    }

    private static List<string> ParseWinnerLineBlocks(string raw)
    {
        var knownKeys = WinnerLineBlockKeys.Select(b => b.Key).ToHashSet();
        var blocks = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(knownKeys.Contains)
            .Distinct()
            .ToList();

        return blocks.Count > 0 ? blocks : ["medal", "mention", "count", "role"];
    }

    public static async Task AddRuleAsync(ActivityRoleRule rule)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = con.CreateCommand(
            "INSERT INTO activity_role_rules (rule_id, name, enabled, metric, mode, window_type, window_days, " +
            "scope_ids, exclude_scope_ids, min_activity, threshold_role_id, threshold_value, threshold_comparator, " +
            "auto_revoke, announce_channel_id, announce_message, announce_interval_days, winner_line_blocks, " +
            "medal_rank1, medal_rank2, medal_rank3, medal_other_template, count_divisor, count_suffix, count_monospace, " +
            "created_by, created_at) " +
            "VALUES (@rule_id, @name, @enabled, @metric, @mode, @window_type, @window_days, @scope_ids, @exclude_scope_ids, " +
            "@min_activity, @threshold_role_id, @threshold_value, @threshold_comparator, @auto_revoke, @announce_channel_id, " +
            "@announce_message, @announce_interval_days, @winner_line_blocks, @medal_rank1, @medal_rank2, @medal_rank3, " +
            "@medal_other_template, @count_divisor, @count_suffix, @count_monospace, @created_by, @created_at)");
        BindRuleParameters(cmd, rule);
        cmd.Parameters.AddWithValue("created_by", (long)rule.CreatedBy);
        cmd.Parameters.AddWithValue("created_at", rule.CreatedAt);
        await cmd.ExecuteNonQueryAsync();
        InvalidateCache();
    }

    public static async Task UpdateRuleAsync(ActivityRoleRule rule)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = con.CreateCommand(
            "UPDATE activity_role_rules SET name = @name, enabled = @enabled, metric = @metric, mode = @mode, " +
            "window_type = @window_type, window_days = @window_days, scope_ids = @scope_ids, " +
            "exclude_scope_ids = @exclude_scope_ids, min_activity = @min_activity, threshold_role_id = @threshold_role_id, " +
            "threshold_value = @threshold_value, threshold_comparator = @threshold_comparator, auto_revoke = @auto_revoke, " +
            "announce_channel_id = @announce_channel_id, announce_message = @announce_message, " +
            "announce_interval_days = @announce_interval_days, winner_line_blocks = @winner_line_blocks, " +
            "medal_rank1 = @medal_rank1, medal_rank2 = @medal_rank2, medal_rank3 = @medal_rank3, " +
            "medal_other_template = @medal_other_template, count_divisor = @count_divisor, " +
            "count_suffix = @count_suffix, count_monospace = @count_monospace " +
            "WHERE rule_id = @rule_id");
        BindRuleParameters(cmd, rule);
        await cmd.ExecuteNonQueryAsync();
        InvalidateCache();
    }

    private static void BindRuleParameters(NpgsqlCommand cmd, ActivityRoleRule rule)
    {
        cmd.Parameters.AddWithValue("rule_id", rule.RuleId);
        cmd.Parameters.AddWithValue("name", rule.Name);
        cmd.Parameters.AddWithValue("enabled", rule.Enabled);
        cmd.Parameters.AddWithValue("metric", rule.Metric.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("mode", rule.Mode.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("window_type", rule.WindowType.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("window_days", rule.WindowDays);
        cmd.Parameters.AddWithValue("scope_ids", rule.ScopeIds);
        cmd.Parameters.AddWithValue("exclude_scope_ids", rule.ExcludeScopeIds);
        cmd.Parameters.AddWithValue("min_activity", rule.MinActivity);
        cmd.Parameters.AddWithValue("threshold_role_id", (long)rule.ThresholdRoleId);
        cmd.Parameters.AddWithValue("threshold_value", rule.ThresholdValue);
        cmd.Parameters.AddWithValue("threshold_comparator", rule.ThresholdComparator.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("auto_revoke", rule.AutoRevoke);
        cmd.Parameters.AddWithValue("announce_channel_id", (long)rule.AnnounceChannelId);
        cmd.Parameters.AddWithValue("announce_message", rule.AnnounceMessage);
        cmd.Parameters.AddWithValue("announce_interval_days", rule.AnnounceIntervalDays);
        cmd.Parameters.AddWithValue("winner_line_blocks", string.Join(',', rule.WinnerLineBlocks));
        cmd.Parameters.AddWithValue("medal_rank1", rule.MedalRank1);
        cmd.Parameters.AddWithValue("medal_rank2", rule.MedalRank2);
        cmd.Parameters.AddWithValue("medal_rank3", rule.MedalRank3);
        cmd.Parameters.AddWithValue("medal_other_template", rule.MedalOtherTemplate);
        cmd.Parameters.AddWithValue("count_divisor", rule.CountDivisor <= 0 ? 1 : rule.CountDivisor);
        cmd.Parameters.AddWithValue("count_suffix", rule.CountSuffix);
        cmd.Parameters.AddWithValue("count_monospace", rule.CountMonospace);
    }

    public static async Task DeleteRuleAsync(string ruleId)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        foreach (var table in new[] { "activity_role_tiers", "activity_role_grants" })
        {
            await using var cmdChild = con.CreateCommand($"DELETE FROM {table} WHERE rule_id = @rule_id");
            cmdChild.Parameters.AddWithValue("rule_id", ruleId);
            await cmdChild.ExecuteNonQueryAsync();
        }

        await EligibilityService.RemoveAllForOwnerAsync(OwnerType, ruleId);

        await using var cmd = con.CreateCommand("DELETE FROM activity_role_rules WHERE rule_id = @rule_id");
        cmd.Parameters.AddWithValue("rule_id", ruleId);
        await cmd.ExecuteNonQueryAsync();
        InvalidateCache();
    }

    public static async Task SetLastAnnouncedAsync(string ruleId, long timestamp)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = con.CreateCommand(
            "UPDATE activity_role_rules SET last_announced_at = @timestamp WHERE rule_id = @rule_id");
        cmd.Parameters.AddWithValue("rule_id", ruleId);
        cmd.Parameters.AddWithValue("timestamp", timestamp);
        await cmd.ExecuteNonQueryAsync();
        InvalidateCache();
    }

    #endregion

    #region Tiers

    private static async Task<List<ActivityRoleTier>> GetAllTiersAsync()
    {
        var tiers = new List<ActivityRoleTier>();
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = con.CreateCommand(
            "SELECT tier_id, rule_id, rank_from, rank_to, roleid FROM activity_role_tiers ORDER BY rule_id, rank_from");
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            tiers.Add(new ActivityRoleTier
            {
                TierId = reader.GetString(0),
                RuleId = reader.GetString(1),
                RankFrom = reader.IsDBNull(2) ? 1 : reader.GetInt32(2),
                RankTo = reader.IsDBNull(3) ? 1 : reader.GetInt32(3),
                RoleId = reader.IsDBNull(4) ? 0 : (ulong)reader.GetInt64(4)
            });

        return tiers;
    }

    public static async Task AddTierAsync(ActivityRoleTier tier)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = con.CreateCommand(
            "INSERT INTO activity_role_tiers (tier_id, rule_id, rank_from, rank_to, roleid) " +
            "VALUES (@tier_id, @rule_id, @rank_from, @rank_to, @roleid)");
        cmd.Parameters.AddWithValue("tier_id", tier.TierId);
        cmd.Parameters.AddWithValue("rule_id", tier.RuleId);
        cmd.Parameters.AddWithValue("rank_from", tier.RankFrom);
        cmd.Parameters.AddWithValue("rank_to", tier.RankTo);
        cmd.Parameters.AddWithValue("roleid", (long)tier.RoleId);
        await cmd.ExecuteNonQueryAsync();
        InvalidateCache();
    }

    public static async Task<bool> RemoveTierAsync(string tierId)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = con.CreateCommand("DELETE FROM activity_role_tiers WHERE tier_id = @tier_id");
        cmd.Parameters.AddWithValue("tier_id", tierId);
        var affected = await cmd.ExecuteNonQueryAsync();
        InvalidateCache();
        return affected > 0;
    }

    #endregion

    #region Grants (bookkeeping of roles this system itself granted)

    public static async Task<List<ActivityRoleGrant>> GetGrantsAsync(string ruleId)
    {
        var grants = new List<ActivityRoleGrant>();
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = con.CreateCommand(
            "SELECT rule_id, userid, roleid, rank, granted_at FROM activity_role_grants WHERE rule_id = @rule_id");
        cmd.Parameters.AddWithValue("rule_id", ruleId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            grants.Add(new ActivityRoleGrant
            {
                RuleId = reader.GetString(0),
                UserId = (ulong)reader.GetInt64(1),
                RoleId = reader.IsDBNull(2) ? 0 : (ulong)reader.GetInt64(2),
                Rank = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                GrantedAt = reader.IsDBNull(4) ? 0 : reader.GetInt64(4)
            });

        return grants;
    }

    private static async Task UpsertGrantAsync(string ruleId, ulong userId, ulong roleId, int rank)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = con.CreateCommand(
            "INSERT INTO activity_role_grants (rule_id, userid, roleid, rank, granted_at) " +
            "VALUES (@rule_id, @userid, @roleid, @rank, @granted_at) " +
            "ON CONFLICT (rule_id, userid, roleid) DO UPDATE SET rank = EXCLUDED.rank");
        cmd.Parameters.AddWithValue("rule_id", ruleId);
        cmd.Parameters.AddWithValue("userid", (long)userId);
        cmd.Parameters.AddWithValue("roleid", (long)roleId);
        cmd.Parameters.AddWithValue("rank", rank);
        cmd.Parameters.AddWithValue("granted_at", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task RemoveGrantAsync(string ruleId, ulong userId, ulong roleId)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = con.CreateCommand(
            "DELETE FROM activity_role_grants WHERE rule_id = @rule_id AND userid = @userid AND roleid = @roleid");
        cmd.Parameters.AddWithValue("rule_id", ruleId);
        cmd.Parameters.AddWithValue("userid", (long)userId);
        cmd.Parameters.AddWithValue("roleid", (long)roleId);
        await cmd.ExecuteNonQueryAsync();
    }

    #endregion

    #region Window

    public static long ResolveWindow(ActivityRoleRule rule, DateTimeOffset now)
    {
        switch (rule.WindowType)
        {
            case ActivityRoleWindowType.CalendarDaily:
                return new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero).ToUnixTimeSeconds();
            case ActivityRoleWindowType.CalendarWeekly:
            {
                var diff = ((int)now.UtcDateTime.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
                var monday = now.UtcDateTime.Date.AddDays(-diff);
                return new DateTimeOffset(monday, TimeSpan.Zero).ToUnixTimeSeconds();
            }
            case ActivityRoleWindowType.CalendarMonthly:
                return new DateTimeOffset(new DateTime(now.UtcDateTime.Year, now.UtcDateTime.Month, 1), TimeSpan.Zero)
                    .ToUnixTimeSeconds();
            case ActivityRoleWindowType.AllTime:
                return 0;
            case ActivityRoleWindowType.FixedRange:
                // Reserved for a future fixed start/end date window - not evaluated yet.
                return rule.WindowStart ?? 0;
            default:
                return now.AddDays(-Math.Max(rule.WindowDays, 1)).ToUnixTimeSeconds();
        }
    }

    #endregion

    #region Evaluation

    /// <summary>
    ///     Pure computation, no side effects - shared by the real evaluation and the WebUI preview.
    /// </summary>
    public static async Task<List<RankedGrant>> ComputeDesiredGrantsAsync(ActivityRoleRule rule, DiscordGuild guild)
    {
        var sinceUnix = ResolveWindow(rule, DateTimeOffset.UtcNow);
        var conditions = await EligibilityService.GetConditionsAsync(OwnerType, rule.RuleId);
        var desired = new List<RankedGrant>();

        if (rule.Mode == ActivityRoleMode.TopN)
        {
            if (rule.Tiers.Count == 0) return desired;

            var maxRank = rule.Tiers.Max(t => t.RankTo);
            var fetchLimit = Math.Max(maxRank * 3, 50);
            var minCount = Math.Max(rule.MinActivity, 1);
            var candidates = rule.Metric == ActivityRoleMetric.Combined
                ? await MetricsQueryService.GetRankedUsersCombinedAsync(sinceUnix, null, rule.ScopeIds,
                    rule.ExcludeScopeIds, minCount, fetchLimit, guild)
                : await MetricsQueryService.GetRankedUsersAsync(MetricsQueryService.MetricTableFor(rule.Metric),
                    sinceUnix, null, rule.ScopeIds, rule.ExcludeScopeIds, minCount, fetchLimit, guild);

            var rank = 0;
            foreach (var candidate in candidates)
            {
                if (rank >= maxRank) break;
                if (!guild.Members.TryGetValue(candidate.UserId, out var member) || member.IsBot) continue;
                if (!await EligibilityService.ConditionsMetAsync(member, conditions)) continue;

                rank++;
                foreach (var tier in rule.Tiers.Where(t => rank >= t.RankFrom && rank <= t.RankTo && t.RoleId != 0))
                    desired.Add(new RankedGrant(member.Id, tier.RoleId, rank, candidate.Count));
            }
        }
        else
        {
            if (rule.ThresholdRoleId == 0) return desired;

            var lte = rule.ThresholdComparator == EligibilityComparator.Lte;
            var overThreshold = rule.Metric == ActivityRoleMetric.Combined
                ? await MetricsQueryService.GetUsersOverThresholdCombinedAsync(sinceUnix, null, rule.ScopeIds,
                    rule.ExcludeScopeIds, lte, rule.ThresholdValue, guild)
                : await MetricsQueryService.GetUsersOverThresholdAsync(MetricsQueryService.MetricTableFor(rule.Metric),
                    sinceUnix, null, rule.ScopeIds, rule.ExcludeScopeIds, lte, rule.ThresholdValue, guild);

            foreach (var userId in overThreshold)
            {
                if (!guild.Members.TryGetValue(userId, out var member) || member.IsBot) continue;
                if (!await EligibilityService.ConditionsMetAsync(member, conditions)) continue;

                desired.Add(new RankedGrant(userId, rule.ThresholdRoleId, 0, 0));
            }
        }

        return desired;
    }

    public static async Task EvaluateRuleAsync(ActivityRoleRule rule)
    {
        if (!rule.Enabled) return;

        var guild = CurrentApplication.TargetGuild;
        if (guild == null) return;

        var desired = await ComputeDesiredGrantsAsync(rule, guild);
        await ApplyDesiredGrantsAsync(rule, guild, desired);

        if (rule.AnnounceChannelId == 0 || rule.AnnounceIntervalDays <= 0) return;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (now - rule.LastAnnouncedAt < rule.AnnounceIntervalDays * 86400L) return;

        await SendAnnouncementAsync(rule, guild, desired);
        await SetLastAnnouncedAsync(rule.RuleId, now);
    }

    private static async Task ApplyDesiredGrantsAsync(ActivityRoleRule rule, DiscordGuild guild,
        List<RankedGrant> desired)
    {
        var current = await GetGrantsAsync(rule.RuleId);
        var desiredKeys = desired.Select(d => (d.UserId, d.RoleId)).ToHashSet();

        // Top-N is inherently exclusive (falling out of the ranks must free the role); threshold roles
        // only get taken away again when the rule owner explicitly opted into auto-revoke.
        var revokeStale = rule.Mode == ActivityRoleMode.TopN || rule.AutoRevoke;
        if (revokeStale)
            foreach (var grant in current.Where(g => !desiredKeys.Contains((g.UserId, g.RoleId))))
                await RevokeAsync(guild, rule, grant.UserId, grant.RoleId);

        foreach (var grant in desired)
        {
            var existing = current.FirstOrDefault(g => g.UserId == grant.UserId && g.RoleId == grant.RoleId);
            if (existing != null)
            {
                if (existing.Rank != grant.Rank) await UpsertGrantAsync(rule.RuleId, grant.UserId, grant.RoleId, grant.Rank);
                continue;
            }

            await GrantAsync(guild, rule, grant.UserId, grant.RoleId, grant.Rank);
        }
    }

    /// <summary>
    ///     Only ever records a grant row when this system itself calls GrantRoleAsync - a role a member
    ///     already holds is left untouched and never adopted into our bookkeeping, so a manually assigned
    ///     role can never be revoked by <see cref="RevokeAsync" /> later.
    /// </summary>
    private static async Task GrantAsync(DiscordGuild guild, ActivityRoleRule rule, ulong userId, ulong roleId,
        int rank)
    {
        var role = guild.GetRole(roleId);
        if (role == null) return;
        if (!guild.Members.TryGetValue(userId, out var member)) return;
        if (member.Roles.Any(r => r.Id == roleId)) return;

        try
        {
            await member.GrantRoleAsync(role, $"ActivityRole: {rule.Name}");
            await UpsertGrantAsync(rule.RuleId, userId, roleId, rank);
        }
        catch (NotFoundException)
        {
        }
        catch (Exception e)
        {
            CurrentApplication.Logger.Error(e,
                "ActivityRoles: failed to grant role {RoleId} to member {UserId} for rule {RuleId}", roleId, userId,
                rule.RuleId);
        }
    }

    private static async Task RevokeAsync(DiscordGuild guild, ActivityRoleRule rule, ulong userId, ulong roleId)
    {
        try
        {
            var role = guild.GetRole(roleId);
            if (role != null && guild.Members.TryGetValue(userId, out var member) &&
                member.Roles.Any(r => r.Id == roleId))
                await member.RevokeRoleAsync(role, $"ActivityRole: {rule.Name}");
        }
        catch (NotFoundException)
        {
        }
        catch (Exception e)
        {
            CurrentApplication.Logger.Error(e,
                "ActivityRoles: failed to revoke role {RoleId} from member {UserId} for rule {RuleId}", roleId,
                userId, rule.RuleId);
        }
        finally
        {
            // Drop our own bookkeeping regardless of whether the Discord call succeeded - a role that
            // is gone, a member who left, or a member who no longer has it are all "nothing left to own".
            await RemoveGrantAsync(rule.RuleId, userId, roleId);
        }
    }

    #endregion

    #region Preview & Announcements

    public static async Task<List<RankedGrant>> PreviewRuleAsync(string ruleId)
    {
        var rule = await GetRuleAsync(ruleId);
        var guild = CurrentApplication.TargetGuild;
        if (rule == null || guild == null) return [];

        return await ComputeDesiredGrantsAsync(rule, guild);
    }

    private static readonly Regex RankPlaceholder = new(@"\{rank(\d+)\}", RegexOptions.Compiled);

    /// <summary>
    ///     The template is free text the rule owner writes themselves (title, explanation, perks, small
    ///     print, ...) - this only fills in the activity data. {rank1}/{rank2}/... let them place each
    ///     rank exactly where they want (own line, own section, mixed with other text); {winners} is the
    ///     lazy all-at-once alternative for anyone who does not care about layout.
    /// </summary>
    private static async Task SendAnnouncementAsync(ActivityRoleRule rule, DiscordGuild guild,
        List<RankedGrant> winners)
    {
        if (rule.AnnounceChannelId == 0) return;
        if (winners.Count == 0) return;
        if (!guild.Channels.TryGetValue(rule.AnnounceChannelId, out var channel)) return;

        var byRank = winners.Where(w => w.Rank > 0).ToDictionary(w => w.Rank);
        var joined = winners
            .OrderBy(w => w.Rank == 0 ? int.MaxValue : w.Rank)
            .Select(w => FormatWinnerLine(w, rule));

        var template = string.IsNullOrWhiteSpace(rule.AnnounceMessage) ? "{winners}" : rule.AnnounceMessage;
        var message = RankPlaceholder.Replace(template,
            m => byRank.TryGetValue(int.Parse(m.Groups[1].Value), out var winner)
                ? FormatWinnerLine(winner, rule)
                : "");
        message = message.Replace("{winners}", string.Join("\n", joined));

        try
        {
            await channel.SendMessageAsync(message);
        }
        catch (Exception e)
        {
            CurrentApplication.Logger.Error(e, "ActivityRoles: failed to send announcement for rule {RuleId}",
                rule.RuleId);
        }
    }

    /// <summary>
    ///     Renders one winner as the ordered set of blocks the rule owner picked (see
    ///     <see cref="WinnerLineBlockKeys" />), with the rule's own medal texts and count formatting.
    ///     None for threshold mode (rank 0, medal renders empty). Public so the WebUI can render a live
    ///     builder preview with sample data without duplicating this logic.
    /// </summary>
    public static string FormatWinnerLine(RankedGrant winner, ActivityRoleRule rule)
    {
        string RenderBlock(string block)
        {
            return block switch
            {
                "medal" => winner.Rank switch
                {
                    1 => rule.MedalRank1,
                    2 => rule.MedalRank2,
                    3 => rule.MedalRank3,
                    > 3 => rule.MedalOtherTemplate.Replace("{rank}", winner.Rank.ToString()),
                    _ => ""
                },
                "mention" => $"<@{winner.UserId}>",
                "count" => FormatCount(winner.Count, rule),
                "role" => $"<@&{winner.RoleId}>",
                _ => ""
            };
        }

        return string.Join(" ", rule.WinnerLineBlocks.Select(RenderBlock).Where(p => p.Length > 0));
    }

    private static string FormatCount(long count, ActivityRoleRule rule)
    {
        var divisor = rule.CountDivisor <= 0 ? 1 : rule.CountDivisor;
        var text = $"{count / divisor}{rule.CountSuffix}";
        return rule.CountMonospace ? $"`{text}`" : text;
    }

    #endregion
}
