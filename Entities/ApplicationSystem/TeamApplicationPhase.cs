#region

using AGC_Management.Enums.ApplicationSystem;

#endregion

namespace AGC_Management.Entities.ApplicationSystem;

public class TeamApplicationPhase
{
    public string PhaseId { get; set; } = "";
    public string PositionId { get; set; } = "";
    public string Name { get; set; } = "";
    public TeamApplicationPhaseState State { get; set; } = TeamApplicationPhaseState.Draft;
    public int QuestionSetVersion { get; set; }
    public long OpensAt { get; set; }
    public long ClosesAt { get; set; }
    public ulong CreatedBy { get; set; }
    public long CreatedAt { get; set; }
}
