namespace AGC_Management.Entities.InfoPanels;

/// <summary>One component row of a panel: either a string select or a row of buttons.</summary>
public class InfoPanelGroup
{
    public const string KindSelect = "select";
    public const string KindButtons = "buttons";

    public string Id { get; set; } = "";
    public string PanelId { get; set; } = "";
    public string Kind { get; set; } = KindSelect;

    public string Placeholder { get; set; } = "";

    /// <summary>
    ///     When set, the panel header carries a line like "Stand der Regeln: 06.01.2025" built from the
    ///     newest page in this group. Empty means the group contributes no date line.
    /// </summary>
    public string DateLabel { get; set; } = "";

    public int Position { get; set; }
    public bool Enabled { get; set; } = true;

    public List<InfoPanelPage> Pages { get; set; } = [];

    public bool IsSelect => Kind == KindSelect;

    /// <summary>Newest edit among the enabled pages, or 0 when the group has none.</summary>
    public long LastUpdatedUnix =>
        Pages.Where(page => page.Enabled).Select(page => page.UpdatedAt).DefaultIfEmpty(0).Max();
}
