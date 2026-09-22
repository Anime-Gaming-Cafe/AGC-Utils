namespace AGC_Management.Entities.InfoPanels;

/// <summary>
///     One select option or one button. A text page answers with an ephemeral embed, a link page is a
///     link button and has no content of its own.
/// </summary>
public class InfoPanelPage
{
    public const string KindText = "text";
    public const string KindLink = "link";

    public string Id { get; set; } = "";
    public string GroupId { get; set; } = "";
    public string PanelId { get; set; } = "";
    public string Kind { get; set; } = KindText;

    public string Label { get; set; } = "";
    public string Description { get; set; } = "";
    public string Emoji { get; set; } = "";

    public int ButtonStyle { get; set; } = (int)DisCatSharp.Enums.ButtonStyle.Primary;
    public string Url { get; set; } = "";

    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string ImageUrl { get; set; } = "";

    /// <summary>Empty falls back to the panel colour.</summary>
    public string Color { get; set; } = "";

    public int Position { get; set; }
    public bool Enabled { get; set; } = true;

    /// <summary>Last time title or content changed. Feeds the panel's date lines.</summary>
    public long UpdatedAt { get; set; }

    public bool IsLink => Kind == KindLink;
}
