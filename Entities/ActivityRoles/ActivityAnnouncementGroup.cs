namespace AGC_Management.Entities.ActivityRoles;

/// <summary>
///     Bundles two or more activity-role rules into one shared announcement message. Purely a reporting
///     concept - it never touches role granting, which stays entirely per-rule.
/// </summary>
public class ActivityAnnouncementGroup
{
    public string GroupId { get; set; } = "";
    public string Name { get; set; } = "";
    public ulong ChannelId { get; set; }
    public int IntervalDays { get; set; }
    public string Message { get; set; } = "";
    public long LastAnnouncedAt { get; set; }
    public ulong CreatedBy { get; set; }
    public long CreatedAt { get; set; }

    public List<ActivityAnnouncementGroupRule> Rules { get; set; } = [];
}
