#region

using AGC_Management.Enums.ActivityRoles;
using AGC_Management.Enums.Conditions;

#endregion

namespace AGC_Management.Entities.ActivityRoles;

public class ActivityRoleRule
{
    public string RuleId { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public ActivityRoleMetric Metric { get; set; } = ActivityRoleMetric.Messages;
    public ActivityRoleMode Mode { get; set; } = ActivityRoleMode.TopN;
    public ActivityRoleWindowType WindowType { get; set; } = ActivityRoleWindowType.Rolling;
    public int WindowDays { get; set; } = 7;
    public long? WindowStart { get; set; }
    public long? WindowEnd { get; set; }
    public long[] ScopeIds { get; set; } = [];
    public long[] ExcludeScopeIds { get; set; } = [];
    public long MinActivity { get; set; }
    public ulong ThresholdRoleId { get; set; }
    public long ThresholdValue { get; set; }
    public EligibilityComparator ThresholdComparator { get; set; } = EligibilityComparator.Gte;
    public bool AutoRevoke { get; set; } = true;
    public ulong AnnounceChannelId { get; set; }
    public string AnnounceMessage { get; set; } = "";
    public ulong CreatedBy { get; set; }
    public long CreatedAt { get; set; }
    public string LastPeriodKey { get; set; } = "";

    public List<ActivityRoleTier> Tiers { get; set; } = [];
}
