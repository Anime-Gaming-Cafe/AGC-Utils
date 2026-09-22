namespace AGC_Management.Entities.Ticket;

/// <summary>An open ticket as the team queue on the dashboard shows it.</summary>
public class TeamTicketRow
{
    public string TicketId { get; set; } = "";
    public string CategoryId { get; set; } = "";
    public string CategoryLabel { get; set; } = "";
    public ulong OwnerId { get; set; }
    public string OwnerName { get; set; } = "";
    public ulong ChannelId { get; set; }
    public string? ChannelName { get; set; }
    public string? ChannelUrl { get; set; }
    public long OpenedAt { get; set; }
    public long LastActivity { get; set; }
    public bool Claimed { get; set; }
    public ulong ClaimedBy { get; set; }
    public string? ClaimedByName { get; set; }
}
