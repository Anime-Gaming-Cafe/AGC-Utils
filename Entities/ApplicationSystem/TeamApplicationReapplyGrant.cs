namespace AGC_Management.Entities.ApplicationSystem;

public class TeamApplicationReapplyGrant
{
    public string GrantId { get; set; } = "";
    public ulong UserId { get; set; }
    public string PositionId { get; set; } = "";
    public string PhaseId { get; set; } = "";
    public ulong GrantedBy { get; set; }
    public long GrantedAt { get; set; }
    public string Reason { get; set; } = "";
    public long UsedAt { get; set; }
}
