namespace AGC_Management.Entities.ActivityRoles;

/// <summary>
///     One member's raw metric count for a rule's window, before eligibility filtering. Position in a
///     rank-ordered list of these (after ineligible candidates are dropped) is the actual rank.
/// </summary>
public class ActivityRoleCandidate
{
    public ulong UserId { get; set; }
    public long Count { get; set; }
}
