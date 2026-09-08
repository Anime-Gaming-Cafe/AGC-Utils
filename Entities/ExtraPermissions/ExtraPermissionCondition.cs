#region

using AGC_Management.Enums.ExtraPermissions;

#endregion

namespace AGC_Management.Entities.ExtraPermissions;

public class ExtraPermissionCondition
{
    public string ConditionId { get; set; } = "";
    public string PermName { get; set; } = "";
    public int GroupId { get; set; } = 1;
    public ExtraPermissionConditionType Type { get; set; } = ExtraPermissionConditionType.Level;
    public ExtraPermissionComparator Comparator { get; set; } = ExtraPermissionComparator.Gte;
    public long Value { get; set; }
    public long[] ScopeIds { get; set; } = [];
    public int WindowDays { get; set; }
    public bool Negate { get; set; }
    public long CreatedAt { get; set; }

    public bool SupportsScope => Type is ExtraPermissionConditionType.Voice or ExtraPermissionConditionType.Messages
        or ExtraPermissionConditionType.VoiceMinutes;

    public bool SupportsWindow => Type is ExtraPermissionConditionType.Messages
        or ExtraPermissionConditionType.VoiceMinutes;

    public bool SupportsComparator => Type is ExtraPermissionConditionType.Level
        or ExtraPermissionConditionType.MembershipAge or ExtraPermissionConditionType.Messages
        or ExtraPermissionConditionType.VoiceMinutes;

    public bool IsThreshold => Type is ExtraPermissionConditionType.Messages
        or ExtraPermissionConditionType.VoiceMinutes;

    /// <summary>
    ///     Evaluation order. Tier 0 is answered from the gateway cache, tier 1 costs one indexed row
    ///     lookup, tier 2 an aggregate. Cheap conditions run first so an OR-sibling can satisfy a group,
    ///     or an AND-group can fail the permission, before an aggregate is ever issued.
    /// </summary>
    public int Tier => Type switch
    {
        ExtraPermissionConditionType.Join or ExtraPermissionConditionType.Voice
            or ExtraPermissionConditionType.Role or ExtraPermissionConditionType.MembershipAge => 0,
        ExtraPermissionConditionType.Level or ExtraPermissionConditionType.Boost => 1,
        _ => 2
    };
}
