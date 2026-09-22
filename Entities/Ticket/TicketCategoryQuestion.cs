namespace AGC_Management.Entities.Ticket;

/// <summary>
///     A field of the intake modal. Discord modals only carry text inputs, so there is no choice or
///     number type here the way the application system has them.
/// </summary>
public class TicketCategoryQuestion
{
    /// <summary>Discord allows at most five components in a modal.</summary>
    public const int MaxPerCategory = 5;

    public string Id { get; set; } = "";
    public string CategoryId { get; set; } = "";
    public int Position { get; set; }
    public string Label { get; set; } = "";
    public string Placeholder { get; set; } = "";

    /// <summary><c>short</c> or <c>long</c>.</summary>
    public string Style { get; set; } = "short";

    public bool Required { get; set; } = true;
    public int MinLength { get; set; }
    public int MaxLength { get; set; }

    public bool IsLong => Style == "long";
}
