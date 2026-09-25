namespace AGC_Management.Entities.Selfroles;

/// <summary>
///     The one persistent message members pick their roles from. It only carries a button per category;
///     the roles themselves live in the ephemeral menu that button opens, because a shared message
///     cannot show a viewer which roles they already have.
/// </summary>
public class SelfrolePanel
{
    public const string PanelId = "selfroles";

    public const string ModeGuild = "guild";
    public const string ModeUrl = "url";
    public const string ModeNone = "none";

    public string Id { get; set; } = PanelId;

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
    public string Color { get; set; } = "2F84A2";

    /// <summary>Put the message back when it was deleted. Only ever fires once a panel was sent.</summary>
    public bool AutoRepost { get; set; } = true;

    /// <summary>Hash of everything the message renders from, so an unchanged pass edits nothing.</summary>
    public string RenderedHash { get; set; } = "";

    public List<SelfroleCategory> Categories { get; set; } = [];

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
                return new DiscordColor("2F84A2");
            }
        }
    }
}
