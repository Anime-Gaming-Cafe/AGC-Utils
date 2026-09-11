#region

using AGC_Management.Enums.ApplicationSystem;

#endregion

namespace AGC_Management.Entities.ApplicationSystem;

public class TeamApplication
{
    public string ApplicationId { get; set; } = "";
    public ulong UserId { get; set; }
    public string PositionId { get; set; } = "";
    public string PhaseId { get; set; } = "";
    public int QuestionSetVersion { get; set; }
    public int Attempt { get; set; } = 1;
    public TeamApplicationStatus Status { get; set; } = TeamApplicationStatus.Eingereicht;
    public long SubmittedAt { get; set; }
    public long DecidedAt { get; set; }
    public ulong DecidedBy { get; set; }
    public string DecisionText { get; set; } = "";
    public bool? DmDelivered { get; set; }
    public string DmError { get; set; } = "";
    public long WithdrawnAt { get; set; }
    public List<TeamApplicationReader> Readers { get; set; } = [];
    public int LevelSnapshot { get; set; }
    public int XpSnapshot { get; set; }
    public long JoinedAtSnapshot { get; set; }
    public long AccountCreatedSnapshot { get; set; }

    public string PositionName { get; set; } = "";
    public string PhaseName { get; set; } = "";

    public bool IsDecided => Status is TeamApplicationStatus.Angenommen or TeamApplicationStatus.Abgelehnt;
    public bool IsWithdrawn => Status == TeamApplicationStatus.Zurueckgezogen;
    public bool IsOpen => !IsDecided && !IsWithdrawn;
}
