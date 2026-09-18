namespace AGC_Management.Entities.ActivityRoles;

public class ActivityRoleGrant
{
    public string RuleId { get; set; } = "";
    public ulong UserId { get; set; }
    public ulong RoleId { get; set; }
    public int Rank { get; set; }
    public long GrantedAt { get; set; }
}
