namespace AGC_Management.Entities.Autoposts;

public class AutopostStep
{
    /// <summary>Wait after the previous step. Ignored on the first step.</summary>
    public int DelaySeconds { get; set; }

    public List<AutopostVariant> Variants { get; set; } = [new()];
}

public class AutopostVariant
{
    public string Content { get; set; } = "";
    public AutopostEmbed Embed { get; set; } = new();
    public List<AutopostButton> Buttons { get; set; } = [];
}

public class AutopostEmbed
{
    public string AuthorName { get; set; } = "";
    public string AuthorIconUrl { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Color { get; set; } = "2F84A2";
    public string ThumbnailUrl { get; set; } = "";
    public string ImageUrl { get; set; } = "";
    public string Footer { get; set; } = "";

    public bool IsEmpty => string.IsNullOrWhiteSpace(AuthorName) && string.IsNullOrWhiteSpace(Title) &&
                           string.IsNullOrWhiteSpace(Description) && string.IsNullOrWhiteSpace(ImageUrl) &&
                           string.IsNullOrWhiteSpace(ThumbnailUrl) && string.IsNullOrWhiteSpace(Footer);
}

public class AutopostButton
{
    public string Label { get; set; } = "";
    public string Url { get; set; } = "";
    public string Emoji { get; set; } = "";
}
