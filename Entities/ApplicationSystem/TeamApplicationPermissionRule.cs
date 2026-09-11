#region

using AGC_Management.Enums.ApplicationSystem;

#endregion

namespace AGC_Management.Entities.ApplicationSystem;

public class TeamApplicationPermissionRule
{
    public string PermissionId { get; set; } = "";
    public ulong RoleId { get; set; }

    /// <summary>Null means every position; the phase scope is stored but not handed out by the matrix UI.</summary>
    public string? PositionId { get; set; }

    public string? PhaseId { get; set; }
    public TeamApplicationPermission Permission { get; set; }
}
