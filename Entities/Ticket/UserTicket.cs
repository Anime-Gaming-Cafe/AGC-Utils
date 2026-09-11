namespace AGC_Management.Entities.Ticket;

/// <summary>
///     A ticket as its owner or an added member sees it. Only the user transcript is carried; the team
///     transcript stays on the staff side.
/// </summary>
public class UserTicket
{
    public string TicketId { get; set; } = "";
    public string Type { get; set; } = "";
    public bool IsOpen { get; set; }

    /// <summary>0 for tickets from before times were recorded.</summary>
    public long OpenedAt { get; set; }

    public long ClosedAt { get; set; }

    public string? ChannelName { get; set; }
    public string? ChannelUrl { get; set; }
    public string? TranscriptUrl { get; set; }

    public string TypeLabel => Type switch
    {
        "support" => "Support-Ticket",
        "report" => "Report-Ticket",
        _ => "Ticket"
    };
}
