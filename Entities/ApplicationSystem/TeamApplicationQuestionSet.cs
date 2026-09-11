#region

using AGC_Management.Enums.ApplicationSystem;

#endregion

namespace AGC_Management.Entities.ApplicationSystem;

public class TeamApplicationQuestionSet
{
    public string PositionId { get; set; } = "";
    public int Version { get; set; }
    public TeamApplicationQuestionSetState State { get; set; } = TeamApplicationQuestionSetState.Draft;
    public ulong CreatedBy { get; set; }
    public long CreatedAt { get; set; }
}
