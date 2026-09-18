namespace AGC_Management.Entities.ActivityRoles;

/// <summary>One rule linked into an announcement group, addressed in the template as {alias.rank1} etc.</summary>
public class ActivityAnnouncementGroupRule
{
    public string GroupId { get; set; } = "";
    public string RuleId { get; set; } = "";
    public string Alias { get; set; } = "";
}
