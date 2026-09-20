#region

using AGC_Management.Enums.BanRequests;

#endregion

namespace AGC_Management.Entities.BanRequests;

/// <summary>
///     One rung of the ban request escalation ladder. <see cref="DelayMinutes" /> counts from the moment
///     the request was posted, not from the previous stage.
/// </summary>
public class BanRequestStage
{
    public string StageId { get; set; } = "";
    public int Position { get; set; }
    public bool Enabled { get; set; } = true;
    public int DelayMinutes { get; set; }
    public BanRequestStageTarget Target { get; set; } = BanRequestStageTarget.Role;
    public ulong RoleId { get; set; }
}
