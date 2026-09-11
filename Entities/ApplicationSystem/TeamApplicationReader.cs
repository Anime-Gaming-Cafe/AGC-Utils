namespace AGC_Management.Entities.ApplicationSystem;

/// <summary>A team member who opened an application. SeenAt is 0 for views recorded before times were kept.</summary>
public class TeamApplicationReader
{
    public ulong UserId { get; set; }
    public long SeenAt { get; set; }
}
