namespace AGC_Management.Entities.Selfroles;

/// <summary>One role a member can give themselves, as one select option or one toggle button.</summary>
public class SelfroleOption
{
    public string Id { get; set; } = "";
    public string CategoryId { get; set; } = "";
    public ulong RoleId { get; set; }

    public string Label { get; set; } = "";
    public string Description { get; set; } = "";
    public string Emoji { get; set; } = "";

    /// <summary>Activity names that hand out this role automatically. Empty means never.</summary>
    public string[] MatchPatterns { get; set; } = [];

    public int Position { get; set; }
    public bool Enabled { get; set; } = true;
}
