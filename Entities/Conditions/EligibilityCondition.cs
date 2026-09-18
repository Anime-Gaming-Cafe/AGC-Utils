#region

using AGC_Management.Enums.Conditions;

#endregion

namespace AGC_Management.Entities.Conditions;

/// <summary>
///     A single eligibility condition owned by some other feature (owner_type/owner_id), e.g. an
///     activity role rule or, later, a giveaway. Not tied to Extra-Permissions on purpose - that
///     system keeps its own table so it never needs to migrate live data.
/// </summary>
public class EligibilityCondition
{
    public string ConditionId { get; set; } = "";
    public string OwnerType { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public int GroupId { get; set; } = 1;
    public EligibilityConditionType Type { get; set; } = EligibilityConditionType.Role;
    public EligibilityComparator Comparator { get; set; } = EligibilityComparator.Gte;
    public long Value { get; set; }
    public long[] ScopeIds { get; set; } = [];
    public bool Negate { get; set; }
    public long CreatedAt { get; set; }
}
