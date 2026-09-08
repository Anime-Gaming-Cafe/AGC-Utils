namespace AGC_Management.Entities.ExtraPermissions;

public class ExtraPermissionStatus
{
    public ExtraPermission Permission { get; set; } = new();
    public ExtraPermissionMemberState MemberState { get; set; } = new();
    public bool HasRole { get; set; }
    public bool ConditionMet { get; set; }
}
