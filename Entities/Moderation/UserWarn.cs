namespace AGC_Management.Entities.Moderation;

/// <summary>
///     An active warning as its own recipient sees it. Only currently active rows from "warns" are
///     represented here; expired warns are moved into "flags" and are not surfaced to the user.
/// </summary>
public class UserWarn
{
    public string CaseId { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsPermanent { get; set; }

    /// <summary>Unix seconds, column "datum".</summary>
    public long Timestamp { get; set; }
}
