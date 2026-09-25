#region

using System.Text.Json.Serialization;
using AGC_Management.Utils;

#endregion

namespace AGC_Management.Entities.Selfroles;

/// <summary>
///     One button on the panel and the menu behind it. The category is also the scope a selection
///     replaces: picking in it never touches a role from another category.
/// </summary>
public class SelfroleCategory
{
    public const string KindSelect = "select";
    public const string KindButtons = "buttons";

    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Emoji { get; set; } = "";
    public int ButtonStyle { get; set; } = (int)DisCatSharp.Enums.ButtonStyle.Secondary;

    public string Kind { get; set; } = KindSelect;
    public string Placeholder { get; set; } = "";

    public int MinValues { get; set; }

    /// <summary>0 means unlimited and resolves to the option count when the menu is built.</summary>
    public int MaxValues { get; set; }

    public bool AllowClear { get; set; } = true;
    public bool Sticky { get; set; } = true;
    public bool AutoAssign { get; set; }

    public int Position { get; set; }
    public bool Enabled { get; set; } = true;

    public List<SelfroleOption> Options { get; set; } = [];

    public bool IsSelect => Kind == KindSelect;
    public bool IsExclusive => MaxValues == 1;

    /// <summary>Ignored when cloning: it is a view of <see cref="Options" /> and would double the payload.</summary>
    [JsonIgnore]
    public List<SelfroleOption> ActiveOptions =>
        [.. Options.Where(option => option.Enabled).OrderBy(option => option.Position)];

    /// <summary>How many components the menu needs, since Discord caps a select at 25 options.</summary>
    public int ChunkSize => IsSelect ? DiscordLimits.SelectOptionsPerMenu : DiscordLimits.ButtonsPerRow;

    public int ChunkCount => (int)Math.Ceiling(ActiveOptions.Count / (double)ChunkSize);

    public int ResolveMaxValues(int optionCount)
    {
        return MaxValues <= 0 ? Math.Max(1, optionCount) : Math.Min(MaxValues, Math.Max(1, optionCount));
    }
}
