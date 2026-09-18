namespace AGC_Management.Entities.ActivityRoles;

public class ActivityRoleTier
{
    public string TierId { get; set; } = "";
    public string RuleId { get; set; } = "";
    public int RankFrom { get; set; } = 1;
    public int RankTo { get; set; } = 1;
    public ulong RoleId { get; set; }
}
