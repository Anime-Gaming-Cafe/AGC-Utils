#region

using System.Text;
using AGC_Management.Entities.ExtraPermissions;
using AGC_Management.Enums.ExtraPermissions;
using AGC_Management.Utils;
using DisCatSharp.Exceptions;

#endregion

namespace AGC_Management.Services;

public static class ExtraPermissionService
{
    public const string Section = "ExtraPermissions";

    private static readonly TimeSpan PermissionCacheTtl = TimeSpan.FromSeconds(30);
    private static List<ExtraPermission>? _permissionCache;
    private static DateTime _permissionCacheExpires = DateTime.MinValue;
    private static bool _migrationDone;

    #region Settings

    public static async Task<bool> GetGlobalAutoRevokeAsync()
    {
        return await RuntimeSettings.GetBoolAsync(Section, "AutoRevokeOnConditionLoss", false);
    }

    public static Task SetGlobalAutoRevokeAsync(bool enabled)
    {
        return RuntimeSettings.SetAsync(Section, "AutoRevokeOnConditionLoss", enabled.ToString());
    }

    public static async Task<bool> ResolveAutoRevokeAsync(ExtraPermission permission)
    {
        return permission.AutoRevoke switch
        {
            ExtraPermissionAutoRevoke.On => true,
            ExtraPermissionAutoRevoke.Off => false,
            _ => await GetGlobalAutoRevokeAsync()
        };
    }

    #endregion

    #region Permission definitions

    public static string Slugify(string name)
    {
        var builder = new StringBuilder();
        foreach (var c in name.Trim().ToLowerInvariant())
            if (char.IsLetterOrDigit(c))
                builder.Append(c);
            else if (c == ' ' || c == '-' || c == '_')
                builder.Append('-');

        var slug = builder.ToString().Trim('-');
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug;
    }

    public static async Task<List<ExtraPermission>> GetPermissionsAsync()
    {
        var cached = _permissionCache;
        if (cached != null && _permissionCacheExpires > DateTime.UtcNow) return cached;

        var permissions = new List<ExtraPermission>();
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using (var cmd = con.CreateCommand(
                         "SELECT permname, displayname, description, roleid, trigger_mode, auto_revoke, created_by, created_at FROM extra_permissions ORDER BY permname"))
        {
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) permissions.Add(ReadPermission(reader));
        }

        if (permissions.Count > 0)
        {
            var byName = permissions.ToDictionary(p => p.PermName);
            foreach (var condition in await GetConditionsAsync(null))
                if (byName.TryGetValue(condition.PermName, out var permission))
                    permission.Conditions.Add(condition);
        }

        _permissionCache = permissions;
        _permissionCacheExpires = DateTime.UtcNow + PermissionCacheTtl;
        return permissions;
    }

    private static void InvalidatePermissionCache()
    {
        _permissionCache = null;
    }

    public static async Task<ExtraPermission?> GetPermissionAsync(string permName)
    {
        var permissions = await GetPermissionsAsync();
        return permissions.FirstOrDefault(p => p.PermName == permName);
    }

    private static ExtraPermission ReadPermission(NpgsqlDataReader reader)
    {
        return new ExtraPermission
        {
            PermName = reader.GetString(0),
            DisplayName = reader.IsDBNull(1) ? reader.GetString(0) : reader.GetString(1),
            Description = reader.IsDBNull(2) ? "" : reader.GetString(2),
            RoleId = (ulong)reader.GetInt64(3),
            TriggerMode = ParseEnum(reader.IsDBNull(4) ? "recurring" : reader.GetString(4),
                ExtraPermissionTriggerMode.Recurring),
            AutoRevoke = ParseEnum(reader.IsDBNull(5) ? "inherit" : reader.GetString(5),
                ExtraPermissionAutoRevoke.Inherit),
            CreatedBy = reader.IsDBNull(6) ? 0 : (ulong)reader.GetInt64(6),
            CreatedAt = reader.IsDBNull(7) ? 0 : reader.GetInt64(7)
        };
    }

    private static T ParseEnum<T>(string raw, T fallback) where T : struct, Enum
    {
        return Enum.TryParse<T>(raw, true, out var parsed) ? parsed : fallback;
    }

    public static async Task AddPermissionAsync(ExtraPermission permission)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = con.CreateCommand(
            "INSERT INTO extra_permissions (permname, displayname, description, roleid, trigger_mode, auto_revoke, created_by, created_at) " +
            "VALUES (@permname, @displayname, @description, @roleid, @trigger_mode, @auto_revoke, @created_by, @created_at)");
        cmd.Parameters.AddWithValue("permname", permission.PermName);
        cmd.Parameters.AddWithValue("displayname", permission.DisplayName);
        cmd.Parameters.AddWithValue("description", permission.Description);
        cmd.Parameters.AddWithValue("roleid", (long)permission.RoleId);
        cmd.Parameters.AddWithValue("trigger_mode", permission.TriggerMode.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("auto_revoke", permission.AutoRevoke.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("created_by", (long)permission.CreatedBy);
        cmd.Parameters.AddWithValue("created_at", permission.CreatedAt);
        await cmd.ExecuteNonQueryAsync();
        InvalidatePermissionCache();
    }

    public static async Task UpdatePermissionAsync(ExtraPermission permission)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = con.CreateCommand(
            "UPDATE extra_permissions SET displayname = @displayname, description = @description, roleid = @roleid, " +
            "trigger_mode = @trigger_mode, auto_revoke = @auto_revoke WHERE permname = @permname");
        cmd.Parameters.AddWithValue("permname", permission.PermName);
        cmd.Parameters.AddWithValue("displayname", permission.DisplayName);
        cmd.Parameters.AddWithValue("description", permission.Description);
        cmd.Parameters.AddWithValue("roleid", (long)permission.RoleId);
        cmd.Parameters.AddWithValue("trigger_mode", permission.TriggerMode.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("auto_revoke", permission.AutoRevoke.ToString().ToLowerInvariant());
        await cmd.ExecuteNonQueryAsync();
        InvalidatePermissionCache();
    }

    public static async Task DeletePermissionAsync(string permName)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        foreach (var table in new[] { "extra_permission_members", "extra_permission_conditions" })
        {
            await using var cmdChild = con.CreateCommand($"DELETE FROM {table} WHERE permname = @permname");
            cmdChild.Parameters.AddWithValue("permname", permName);
            await cmdChild.ExecuteNonQueryAsync();
        }

        await using var cmd = con.CreateCommand("DELETE FROM extra_permissions WHERE permname = @permname");
        cmd.Parameters.AddWithValue("permname", permName);
        await cmd.ExecuteNonQueryAsync();
        InvalidatePermissionCache();
    }

    #endregion

    #region Conditions

    public static async Task<List<ExtraPermissionCondition>> GetConditionsAsync(string? permName)
    {
        var conditions = new List<ExtraPermissionCondition>();
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        var sql =
            "SELECT condition_id, permname, group_id, condition_type, comparator, value, scope_ids, window_days, negate, created_at " +
            "FROM extra_permission_conditions";
        if (permName != null) sql += " WHERE permname = @permname";
        sql += " ORDER BY group_id, created_at";

        await using var cmd = con.CreateCommand(sql);
        if (permName != null) cmd.Parameters.AddWithValue("permname", permName);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            conditions.Add(new ExtraPermissionCondition
            {
                ConditionId = reader.GetString(0),
                PermName = reader.GetString(1),
                GroupId = reader.IsDBNull(2) ? 1 : reader.GetInt32(2),
                Type = ParseEnum(reader.IsDBNull(3) ? "level" : reader.GetString(3),
                    ExtraPermissionConditionType.Level),
                Comparator = ParseEnum(reader.IsDBNull(4) ? "gte" : reader.GetString(4),
                    ExtraPermissionComparator.Gte),
                Value = reader.IsDBNull(5) ? 0 : reader.GetInt64(5),
                ScopeIds = reader.IsDBNull(6) ? [] : reader.GetFieldValue<long[]>(6),
                WindowDays = reader.IsDBNull(7) ? 0 : reader.GetInt32(7),
                Negate = !reader.IsDBNull(8) && reader.GetBoolean(8),
                CreatedAt = reader.IsDBNull(9) ? 0 : reader.GetInt64(9)
            });

        return conditions;
    }

    public static async Task AddConditionAsync(ExtraPermissionCondition condition)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = con.CreateCommand(
            "INSERT INTO extra_permission_conditions (condition_id, permname, group_id, condition_type, comparator, value, scope_ids, window_days, negate, created_at) " +
            "VALUES (@condition_id, @permname, @group_id, @condition_type, @comparator, @value, @scope_ids, @window_days, @negate, @created_at)");
        cmd.Parameters.AddWithValue("condition_id", condition.ConditionId);
        cmd.Parameters.AddWithValue("permname", condition.PermName);
        cmd.Parameters.AddWithValue("group_id", condition.GroupId);
        cmd.Parameters.AddWithValue("condition_type", condition.Type.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("comparator", condition.Comparator.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("value", condition.Value);
        cmd.Parameters.AddWithValue("scope_ids", condition.ScopeIds);
        cmd.Parameters.AddWithValue("window_days", condition.WindowDays);
        cmd.Parameters.AddWithValue("negate", condition.Negate);
        cmd.Parameters.AddWithValue("created_at", condition.CreatedAt);
        await cmd.ExecuteNonQueryAsync();
        InvalidatePermissionCache();
    }

    public static async Task<bool> RemoveConditionAsync(string conditionId)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd =
            con.CreateCommand("DELETE FROM extra_permission_conditions WHERE condition_id = @condition_id");
        cmd.Parameters.AddWithValue("condition_id", conditionId);
        var affected = await cmd.ExecuteNonQueryAsync();
        InvalidatePermissionCache();
        return affected > 0;
    }

    /// <summary>
    ///     Moves the pre-condition-system trigger columns into condition rows. Runs once, is a no-op on a
    ///     database that never carried the old shape, and is awaited before any evaluation so a half
    ///     migrated permission can never be evaluated as "no conditions".
    /// </summary>
    public static async Task EnsureMigratedAsync()
    {
        if (_migrationDone) return;

        if (await RuntimeSettings.GetBoolAsync(Section, "ConditionsMigrated", false))
        {
            _migrationDone = true;
            return;
        }

        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        var legacy = new List<(string permName, string triggerType, long triggerValue)>();

        await using (var cmd = con.CreateCommand(
                         "SELECT p.permname, p.trigger_type, p.trigger_value FROM extra_permissions p " +
                         "LEFT JOIN extra_permission_conditions c ON c.permname = p.permname " +
                         "WHERE c.condition_id IS NULL AND p.trigger_type IS NOT NULL AND p.trigger_type <> 'none'"))
        {
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                legacy.Add((reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? 0 : reader.GetInt64(2)));
        }

        foreach (var (permName, triggerType, triggerValue) in legacy)
            await AddConditionAsync(new ExtraPermissionCondition
            {
                ConditionId = ToolSet.GenerateCaseID(),
                PermName = permName,
                GroupId = 1,
                Type = ParseEnum(triggerType, ExtraPermissionConditionType.Level),
                Comparator = ExtraPermissionComparator.Gte,
                Value = triggerValue,
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        if (legacy.Count > 0)
            CurrentApplication.Logger.Information(
                $"ExtraPermissions: {legacy.Count} Legacy-Trigger in Bedingungen überführt.");

        await RuntimeSettings.SetAsync(Section, "ConditionsMigrated", "true");
        _migrationDone = true;
    }

    #endregion

    #region Condition evaluation

    public static async Task<bool> ConditionsMetAsync(DiscordMember member, ExtraPermission permission)
    {
        if (permission.Conditions.Count == 0) return false;

        var groups = permission.Conditions
            .GroupBy(c => c.GroupId)
            .OrderBy(g => g.Min(c => c.Tier));

        foreach (var group in groups)
        {
            var satisfied = false;
            foreach (var condition in group.OrderBy(c => c.Tier))
                if (await EvaluateConditionAsync(member, condition))
                {
                    satisfied = true;
                    break;
                }

            if (!satisfied) return false;
        }

        return true;
    }

    private static async Task<bool> EvaluateConditionAsync(DiscordMember member, ExtraPermissionCondition condition)
    {
        var raw = await EvaluateRawAsync(member, condition);
        return condition.Negate ? !raw : raw;
    }

    private static async Task<bool> EvaluateRawAsync(DiscordMember member, ExtraPermissionCondition condition)
    {
        switch (condition.Type)
        {
            case ExtraPermissionConditionType.Join:
                return true;
            case ExtraPermissionConditionType.Voice:
            {
                var channel = member.VoiceState?.Channel;
                if (channel == null) return false;
                if (condition.ScopeIds.Length == 0) return true;

                return ExpandScope(member.Guild, condition.ScopeIds).Contains(channel.Id);
            }
            case ExtraPermissionConditionType.Role:
                return member.Roles.Any(r => r.Id == (ulong)condition.Value);
            case ExtraPermissionConditionType.MembershipAge:
            {
                if (member.JoinedAt == default) return false;

                var days = (long)(DateTimeOffset.UtcNow - member.JoinedAt).TotalDays;
                return Compare(days, condition);
            }
            case ExtraPermissionConditionType.Boost:
                // Cheap path first; the refetch exists because a cached member can carry a stale
                // null PremiumSince (see BoosterColorService.IsEligibleAsync).
                return member.PremiumSince.HasValue || await BoosterColorService.IsBoostingAsync(member);
            case ExtraPermissionConditionType.Level:
                return Compare(await LevelUtils.GetLevel(member.Id), condition);
            case ExtraPermissionConditionType.Messages:
                return Compare(await CountMetricAsync("metrics_messages", member, condition), condition);
            case ExtraPermissionConditionType.VoiceMinutes:
                return Compare(await CountMetricAsync("metrics_voice", member, condition), condition);
            default:
                return false;
        }
    }

    private static bool Compare(long actual, ExtraPermissionCondition condition)
    {
        return condition.Comparator == ExtraPermissionComparator.Lte
            ? actual <= condition.Value
            : actual >= condition.Value;
    }

    /// <summary>
    ///     Category ids are stored, not their children, so a channel moved into the category starts
    ///     counting without touching the condition.
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

    public static string MetricTableFor(ExtraPermissionConditionType type)
    {
        return type == ExtraPermissionConditionType.Messages ? "metrics_messages" : "metrics_voice";
    }

    private static Task<long> CountMetricAsync(string table, DiscordMember member,
        ExtraPermissionCondition condition)
    {
        return CountMetricAsync(table, member.Id, condition.WindowDays, condition.ScopeIds, member.Guild);
    }

    /// <summary>
    ///     Both metric tables are row logs, so a count is the metric: one row per message, and one row per
    ///     user and minute for voice. A window of 0 counts everything ever recorded.
    /// </summary>
    public static async Task<long> CountMetricAsync(string table, ulong userId, int windowDays,
        long[] scopeIds, DiscordGuild? guild = null)
    {
        var scope = ExpandScope(guild, scopeIds);
        var sql = new StringBuilder($"SELECT COUNT(*) FROM {table} WHERE userid = @userid");
        if (scope.Count > 0) sql.Append(" AND channelid = ANY(@channels)");
        if (windowDays > 0) sql.Append(" AND timestamp >= @since");

        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = con.CreateCommand(sql.ToString());
        cmd.Parameters.AddWithValue("userid", (long)userId);
        if (scope.Count > 0) cmd.Parameters.AddWithValue("channels", scope.Select(x => (long)x).ToArray());
        if (windowDays > 0)
            cmd.Parameters.AddWithValue("since", DateTimeOffset.UtcNow.AddDays(-windowDays).ToUnixTimeSeconds());

        var result = await cmd.ExecuteScalarAsync();
        return result is long count ? count : 0;
    }

    public static async Task<List<ulong>> GetMembersOverThresholdAsync(DiscordGuild guild,
        ExtraPermissionCondition condition)
    {
        var userIds = new List<ulong>();
        var scope = ExpandScope(guild, condition.ScopeIds);
        var sql = new StringBuilder($"SELECT userid FROM {MetricTableFor(condition.Type)} WHERE true");
        if (scope.Count > 0) sql.Append(" AND channelid = ANY(@channels)");
        if (condition.WindowDays > 0) sql.Append(" AND timestamp >= @since");
        sql.Append(" GROUP BY userid HAVING COUNT(*) >= @threshold");

        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = con.CreateCommand(sql.ToString());
        cmd.Parameters.AddWithValue("threshold", condition.Value);
        if (scope.Count > 0) cmd.Parameters.AddWithValue("channels", scope.Select(x => (long)x).ToArray());
        if (condition.WindowDays > 0)
            cmd.Parameters.AddWithValue("since",
                DateTimeOffset.UtcNow.AddDays(-condition.WindowDays).ToUnixTimeSeconds());

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) userIds.Add((ulong)reader.GetInt64(0));

        return userIds;
    }

    #endregion

    #region Member state

    public static async Task<ExtraPermissionMemberState> GetMemberStateAsync(ulong userId, string permName)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = con.CreateCommand(
            "SELECT userid, permname, state, expires_at, actor_id, reason, updated_at, trigger_fired, trigger_fired_at " +
            "FROM extra_permission_members WHERE userid = @userid AND permname = @permname");
        cmd.Parameters.AddWithValue("userid", (long)userId);
        cmd.Parameters.AddWithValue("permname", permName);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return new ExtraPermissionMemberState { UserId = userId, PermName = permName };

        return ReadMemberState(reader);
    }

    public static async Task<Dictionary<string, ExtraPermissionMemberState>> GetMemberStatesAsync(ulong userId)
    {
        var states = new Dictionary<string, ExtraPermissionMemberState>();
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = con.CreateCommand(
            "SELECT userid, permname, state, expires_at, actor_id, reason, updated_at, trigger_fired, trigger_fired_at " +
            "FROM extra_permission_members WHERE userid = @userid");
        cmd.Parameters.AddWithValue("userid", (long)userId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var state = ReadMemberState(reader);
            states[state.PermName] = state;
        }

        return states;
    }

    private static ExtraPermissionMemberState ReadMemberState(NpgsqlDataReader reader)
    {
        return new ExtraPermissionMemberState
        {
            UserId = (ulong)reader.GetInt64(0),
            PermName = reader.GetString(1),
            State = ParseEnum(reader.IsDBNull(2) ? "auto" : reader.GetString(2), ExtraPermissionState.Auto),
            ExpiresAt = reader.IsDBNull(3) ? 0 : reader.GetInt64(3),
            ActorId = reader.IsDBNull(4) ? 0 : (ulong)reader.GetInt64(4),
            Reason = reader.IsDBNull(5) ? "" : reader.GetString(5),
            UpdatedAt = reader.IsDBNull(6) ? 0 : reader.GetInt64(6),
            TriggerFired = !reader.IsDBNull(7) && reader.GetBoolean(7),
            TriggerFiredAt = reader.IsDBNull(8) ? 0 : reader.GetInt64(8)
        };
    }

    public static async Task SetOverrideAsync(ulong userId, string permName, ExtraPermissionState state,
        long expiresAt, ulong actorId, string reason)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = con.CreateCommand(
            "INSERT INTO extra_permission_members (userid, permname, state, expires_at, actor_id, reason, updated_at) " +
            "VALUES (@userid, @permname, @state, @expires_at, @actor_id, @reason, @updated_at) " +
            "ON CONFLICT (userid, permname) DO UPDATE SET state = EXCLUDED.state, expires_at = EXCLUDED.expires_at, " +
            "actor_id = EXCLUDED.actor_id, reason = EXCLUDED.reason, updated_at = EXCLUDED.updated_at");
        cmd.Parameters.AddWithValue("userid", (long)userId);
        cmd.Parameters.AddWithValue("permname", permName);
        cmd.Parameters.AddWithValue("state", state.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("expires_at", expiresAt);
        cmd.Parameters.AddWithValue("actor_id", (long)actorId);
        cmd.Parameters.AddWithValue("reason", reason);
        cmd.Parameters.AddWithValue("updated_at", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task ResetOverrideAsync(ulong userId, string permName, bool rearmTrigger)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        var triggerReset = rearmTrigger ? ", trigger_fired = false, trigger_fired_at = 0" : "";
        await using var cmd = con.CreateCommand(
            "UPDATE extra_permission_members SET state = 'auto', expires_at = 0, actor_id = 0, reason = '', updated_at = @updated_at" +
            triggerReset + " WHERE userid = @userid AND permname = @permname");
        cmd.Parameters.AddWithValue("userid", (long)userId);
        cmd.Parameters.AddWithValue("permname", permName);
        cmd.Parameters.AddWithValue("updated_at", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task MarkTriggerFiredAsync(ulong userId, string permName)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = con.CreateCommand(
            "INSERT INTO extra_permission_members (userid, permname, trigger_fired, trigger_fired_at, updated_at) " +
            "VALUES (@userid, @permname, true, @now, @now) " +
            "ON CONFLICT (userid, permname) DO UPDATE SET trigger_fired = true, " +
            "trigger_fired_at = CASE WHEN extra_permission_members.trigger_fired THEN extra_permission_members.trigger_fired_at ELSE EXCLUDED.trigger_fired_at END");
        cmd.Parameters.AddWithValue("userid", (long)userId);
        cmd.Parameters.AddWithValue("permname", permName);
        cmd.Parameters.AddWithValue("now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task<List<ulong>> GetMembersWithExpiredOverridesAsync()
    {
        var userIds = new List<ulong>();
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = con.CreateCommand(
            "SELECT DISTINCT userid FROM extra_permission_members WHERE state <> 'auto' AND expires_at > 0 AND expires_at <= @now");
        cmd.Parameters.AddWithValue("now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) userIds.Add((ulong)reader.GetInt64(0));

        return userIds;
    }

    public static async Task ClearExpiredOverridesAsync()
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = con.CreateCommand(
            "UPDATE extra_permission_members SET state = 'auto', expires_at = 0, updated_at = @now " +
            "WHERE state <> 'auto' AND expires_at > 0 AND expires_at <= @now");
        cmd.Parameters.AddWithValue("now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        await cmd.ExecuteNonQueryAsync();
    }

    #endregion

    #region Resolver

    /// <summary>
    ///     The single decision point of the whole system. Order matters: a manual override always wins over
    ///     the automatism, and "Untouched" means the role is deliberately left as-is - that is what makes
    ///     "Once" and a disabled auto-revoke work without special-casing every call site.
    /// </summary>
    public static async Task<ExtraPermissionDecision> ResolveAsync(DiscordMember member, ExtraPermission permission,
        ExtraPermissionMemberState state)
    {
        switch (state.EffectiveState)
        {
            case ExtraPermissionState.Revoked:
                return ExtraPermissionDecision.Revoke;
            case ExtraPermissionState.Granted:
                return ExtraPermissionDecision.Grant;
        }

        if (permission.Conditions.Count == 0) return ExtraPermissionDecision.Untouched;

        if (permission.TriggerMode == ExtraPermissionTriggerMode.Once)
        {
            if (state.TriggerFired) return ExtraPermissionDecision.Untouched;
            if (!await ConditionsMetAsync(member, permission)) return ExtraPermissionDecision.Untouched;

            await MarkTriggerFiredAsync(member.Id, permission.PermName);
            return ExtraPermissionDecision.Grant;
        }

        if (await ConditionsMetAsync(member, permission))
        {
            if (!state.TriggerFired) await MarkTriggerFiredAsync(member.Id, permission.PermName);
            return ExtraPermissionDecision.Grant;
        }

        return await ResolveAutoRevokeAsync(permission)
            ? ExtraPermissionDecision.Revoke
            : ExtraPermissionDecision.Untouched;
    }

    #endregion

    #region Apply

    public static async Task<bool> ApplyAsync(DiscordMember member, ExtraPermission permission,
        ExtraPermissionDecision decision)
    {
        if (decision == ExtraPermissionDecision.Untouched) return false;

        var guild = member.Guild ?? CurrentApplication.TargetGuild;
        if (guild == null) return false;

        var role = guild.GetRole(permission.RoleId);
        if (role == null) return false;

        var hasRole = member.Roles.Any(r => r.Id == permission.RoleId);
        if (decision == ExtraPermissionDecision.Grant && hasRole) return false;
        if (decision == ExtraPermissionDecision.Revoke && !hasRole) return false;

        try
        {
            if (decision == ExtraPermissionDecision.Grant)
                await member.GrantRoleAsync(role, $"ExtraPermission: {permission.PermName}");
            else
                await member.RevokeRoleAsync(role, $"ExtraPermission: {permission.PermName}");

            return true;
        }
        catch (NotFoundException)
        {
            return false;
        }
        catch (Exception e)
        {
            CurrentApplication.Logger.Error(e,
                "ExtraPermissions: failed to apply {Permission} for member {MemberId}", permission.PermName,
                member.Id);
            return false;
        }
    }

    public static async Task<bool> EvaluateAsync(DiscordMember member, ExtraPermission permission)
    {
        var state = await GetMemberStateAsync(member.Id, permission.PermName);
        var decision = await ResolveAsync(member, permission, state);
        return await ApplyAsync(member, permission, decision);
    }

    public static async Task EvaluateMemberAsync(DiscordMember member,
        params ExtraPermissionConditionType[] typeFilter)
    {
        if (member == null || member.IsBot) return;

        await EnsureMigratedAsync();

        var permissions = await GetPermissionsAsync();
        if (typeFilter.Length > 0)
            permissions = permissions.Where(p => p.Conditions.Any(c => typeFilter.Contains(c.Type))).ToList();

        if (permissions.Count == 0) return;

        var states = await GetMemberStatesAsync(member.Id);

        foreach (var permission in permissions)
        {
            var state = states.TryGetValue(permission.PermName, out var existing)
                ? existing
                : new ExtraPermissionMemberState { UserId = member.Id, PermName = permission.PermName };

            var decision = await ResolveAsync(member, permission, state);
            await ApplyAsync(member, permission, decision);
        }
    }

    #endregion

    #region Status

    public static async Task<List<ExtraPermissionStatus>> GetStatusAsync(ulong userId, DiscordMember? member)
    {
        var result = new List<ExtraPermissionStatus>();
        var permissions = await GetPermissionsAsync();
        var states = await GetMemberStatesAsync(userId);

        foreach (var permission in permissions)
        {
            var state = states.TryGetValue(permission.PermName, out var existing)
                ? existing
                : new ExtraPermissionMemberState { UserId = userId, PermName = permission.PermName };

            var status = new ExtraPermissionStatus
            {
                Permission = permission,
                MemberState = state,
                HasRole = member != null && member.Roles.Any(r => r.Id == permission.RoleId)
            };

            if (member != null) status.ConditionMet = await ConditionsMetAsync(member, permission);

            result.Add(status);
        }

        return result;
    }

    #endregion
}
