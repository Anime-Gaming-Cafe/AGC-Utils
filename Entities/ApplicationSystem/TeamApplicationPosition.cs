namespace AGC_Management.Entities.ApplicationSystem;

public class TeamApplicationPosition
{
    public string PositionId { get; set; } = "";
    public string PositionName { get; set; } = "";
    public string Description { get; set; } = "";
    public int MinLevel { get; set; } = 20;
    public ulong NotifyChannelId { get; set; }
    public bool Active { get; set; }

    /// <summary>
    ///     Debug switch: accepts submissions regardless of phase state, minimum level and the one
    ///     application per phase rule.
    /// </summary>
    public bool AlwaysOpen { get; set; }
    public long CreatedAt { get; set; }
}
