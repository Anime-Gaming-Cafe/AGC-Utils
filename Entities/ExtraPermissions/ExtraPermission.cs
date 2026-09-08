#region

using AGC_Management.Enums.ExtraPermissions;

#endregion

namespace AGC_Management.Entities.ExtraPermissions;

public class ExtraPermission
{
    public string PermName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public ulong RoleId { get; set; }
    public ExtraPermissionTriggerMode TriggerMode { get; set; } = ExtraPermissionTriggerMode.Recurring;
    public ExtraPermissionAutoRevoke AutoRevoke { get; set; } = ExtraPermissionAutoRevoke.Inherit;
    public ulong CreatedBy { get; set; }
    public long CreatedAt { get; set; }
    public List<ExtraPermissionCondition> Conditions { get; set; } = [];
}
