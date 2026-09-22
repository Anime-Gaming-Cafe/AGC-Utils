namespace AGC_Management.Entities.Ticket;

/// <summary>
///     An answer to an intake question. The label is copied in, so editing the question later does not
///     rewrite what an old ticket was asked.
/// </summary>
public class TicketIntakeAnswer
{
    public string TicketId { get; set; } = "";
    public string QuestionId { get; set; } = "";
    public string QuestionLabel { get; set; } = "";
    public string Answer { get; set; } = "";
    public int Position { get; set; }
}
