#region

using AGC_Management.Entities.Conditions;
using AGC_Management.Enums.Conditions;
using AGC_Management.Utils;

#endregion

namespace AGC_Management.Services;

/// <summary>
///     Generic eligibility-condition evaluator over <c>eligibility_conditions</c>, keyed by
///     (ownerType, ownerId) instead of a feature-specific foreign key. Activity Roles is the first
///     consumer (ownerType = "activityrule"); a future Giveaway system can reuse the same table and
///     evaluator with its own ownerType instead of duplicating the condition model.
///     Note the semantics differ from Extra-Permissions' own condition system: there, conditions are
///     the grant *trigger* (none configured => never grants). Here they are exemption/eligibility
///     filters (none configured => everyone is eligible), so an empty list returns true, not false.
/// </summary>
public static class EligibilityService
{
    public static async Task<List<EligibilityCondition>> GetConditionsAsync(string ownerType, string ownerId)
    {
        var conditions = new List<EligibilityCondition>();
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = con.CreateCommand(
            "SELECT condition_id, owner_type, owner_id, group_id, condition_type, comparator, value, scope_ids, negate, created_at " +
            "FROM eligibility_conditions WHERE owner_type = @owner_type AND owner_id = @owner_id ORDER BY group_id, created_at");
        cmd.Parameters.AddWithValue("owner_type", ownerType);
        cmd.Parameters.AddWithValue("owner_id", ownerId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) conditions.Add(ReadCondition(reader));

        return conditions;
    }

    private static EligibilityCondition ReadCondition(NpgsqlDataReader reader)
    {
        return new EligibilityCondition
        {
            ConditionId = reader.GetString(0),
            OwnerType = reader.GetString(1),
            OwnerId = reader.GetString(2),
            GroupId = reader.IsDBNull(3) ? 1 : reader.GetInt32(3),
            Type = ParseEnum(reader.IsDBNull(4) ? "role" : reader.GetString(4), EligibilityConditionType.Role),
            Comparator = ParseEnum(reader.IsDBNull(5) ? "gte" : reader.GetString(5), EligibilityComparator.Gte),
            Value = reader.IsDBNull(6) ? 0 : reader.GetInt64(6),
            ScopeIds = reader.IsDBNull(7) ? [] : reader.GetFieldValue<long[]>(7),
            Negate = !reader.IsDBNull(8) && reader.GetBoolean(8),
            CreatedAt = reader.IsDBNull(9) ? 0 : reader.GetInt64(9)
        };
    }

    private static T ParseEnum<T>(string raw, T fallback) where T : struct, Enum
    {
        return Enum.TryParse<T>(raw, true, out var parsed) ? parsed : fallback;
    }

    public static async Task AddConditionAsync(EligibilityCondition condition)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = con.CreateCommand(
            "INSERT INTO eligibility_conditions (condition_id, owner_type, owner_id, group_id, condition_type, comparator, value, scope_ids, negate, created_at) " +
            "VALUES (@condition_id, @owner_type, @owner_id, @group_id, @condition_type, @comparator, @value, @scope_ids, @negate, @created_at)");
        cmd.Parameters.AddWithValue("condition_id", condition.ConditionId);
        cmd.Parameters.AddWithValue("owner_type", condition.OwnerType);
        cmd.Parameters.AddWithValue("owner_id", condition.OwnerId);
        cmd.Parameters.AddWithValue("group_id", condition.GroupId);
        cmd.Parameters.AddWithValue("condition_type", condition.Type.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("comparator", condition.Comparator.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("value", condition.Value);
        cmd.Parameters.AddWithValue("scope_ids", condition.ScopeIds);
        cmd.Parameters.AddWithValue("negate", condition.Negate);
        cmd.Parameters.AddWithValue("created_at", condition.CreatedAt);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task<bool> RemoveConditionAsync(string conditionId)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = con.CreateCommand("DELETE FROM eligibility_conditions WHERE condition_id = @condition_id");
        cmd.Parameters.AddWithValue("condition_id", conditionId);
        var affected = await cmd.ExecuteNonQueryAsync();
        return affected > 0;
    }

    public static async Task RemoveAllForOwnerAsync(string ownerType, string ownerId)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = con.CreateCommand(
            "DELETE FROM eligibility_conditions WHERE owner_type = @owner_type AND owner_id = @owner_id");
        cmd.Parameters.AddWithValue("owner_type", ownerType);
        cmd.Parameters.AddWithValue("owner_id", ownerId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    ///     AND-of-OR-groups, same shape as <c>ExtraPermissionService.ConditionsMetAsync</c>: conditions
    ///     sharing a group_id are OR'd, distinct groups are AND'd. An empty list is vacuously true here
    ///     (no exemptions configured), the inverse of Extra-Permissions' trigger semantics.
    /// </summary>
    public static async Task<bool> ConditionsMetAsync(DiscordMember member, List<EligibilityCondition> conditions)
    {
        if (conditions.Count == 0) return true;

        foreach (var group in conditions.GroupBy(c => c.GroupId))
        {
            var satisfied = false;
            foreach (var condition in group)
                if (await EvaluateConditionAsync(member, condition))
                {
                    satisfied = true;
                    break;
                }

            if (!satisfied) return false;
        }

        return true;
    }

    private static async Task<bool> EvaluateConditionAsync(DiscordMember member, EligibilityCondition condition)
    {
        var raw = await EvaluateRawAsync(member, condition);
        return condition.Negate ? !raw : raw;
    }

    private static async Task<bool> EvaluateRawAsync(DiscordMember member, EligibilityCondition condition)
    {
        switch (condition.Type)
        {
            case EligibilityConditionType.Role:
                return member.Roles.Any(r => r.Id == (ulong)condition.Value);
            case EligibilityConditionType.Member:
                return member.Id == (ulong)condition.Value;
            case EligibilityConditionType.MembershipAge:
            {
                if (member.JoinedAt == default) return false;

                var days = (long)(DateTimeOffset.UtcNow - member.JoinedAt).TotalDays;
                return Compare(days, condition);
            }
            case EligibilityConditionType.Boost:
                return member.PremiumSince.HasValue || await BoosterColorService.IsBoostingAsync(member);
            case EligibilityConditionType.Level:
                return Compare(await LevelUtils.GetLevel(member.Id), condition);
            default:
                return false;
        }
    }

    private static bool Compare(long actual, EligibilityCondition condition)
    {
        return condition.Comparator == EligibilityComparator.Lte
            ? actual <= condition.Value
            : actual >= condition.Value;
    }
}
