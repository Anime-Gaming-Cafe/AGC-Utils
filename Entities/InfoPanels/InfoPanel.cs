namespace AGC_Management.Entities.InfoPanels;

/// <summary>
///     One persistent Discord message built from groups and pages. The rules panel is the first one;
///     the model is kept general so selfroles can become a second panel without a schema change.
/// </summary>
public class InfoPanel
{
    public const string ModeGuild = "guild";
    public const string ModeUrl = "url";
    public const string ModeNone = "none";

    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>0 means nobody picked a channel yet, so nothing has been posted.</summary>
    public ulong ChannelId { get; set; }

    public ulong MessageId { get; set; }
    public bool Enabled { get; set; }

    public string HeaderTitle { get; set; } = "";
    public string HeaderText { get; set; } = "";
    public string AuthorName { get; set; } = "";

    public string AuthorIconMode { get; set; } = ModeGuild;
    public string AuthorIconUrl { get; set; } = "";

    public string BannerMode { get; set; } = ModeGuild;
    public string BannerUrl { get; set; } = "";

    /// <summary>Transparent spacer image that forces a constant embed width.</summary>
    public string SpacerUrl { get; set; } = "";

    public string Color { get; set; } = "2F3136";

    /// <summary>Put the message back when it was deleted. Only ever fires once a panel was sent.</summary>
    public bool AutoRepost { get; set; } = true;

    /// <summary>Hash of everything the message renders from, so an unchanged pass edits nothing.</summary>
    public string RenderedHash { get; set; } = "";

    public int SortOrder { get; set; }

    public List<InfoPanelGroup> Groups { get; set; } = [];

    public bool IsPosted => ChannelId != 0 && MessageId != 0;

    public DiscordColor DiscordColor
    {
        get
        {
            try
            {
                return new DiscordColor(Color);
            }
            catch (Exception)
            {
                return new DiscordColor("2F3136");
            }
        }
    }
}
