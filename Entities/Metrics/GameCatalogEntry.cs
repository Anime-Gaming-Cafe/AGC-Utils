namespace AGC_Management.Entities.Metrics;

/// <summary>
///     One game the bot has actually seen somebody play. <see cref="Key" /> is the normalised name and
///     the identity of the row; <see cref="ActivityId" /> is the number every metric row and every later
///     condition refers to, handed out once at first sighting and never changed afterwards.
/// </summary>
public class GameCatalogEntry
{
    public string Key { get; set; } = "";
    public long ActivityId { get; set; }
    public string DisplayName { get; set; } = "";

    /// <summary>Discord's own id for the game, 0 when it never sent one.</summary>
    public ulong ApplicationId { get; set; }

    /// <summary>The selfrole option this game counts as, empty when nobody bound it.</summary>
    public string OptionId { get; set; } = "";

    public long BoundAt { get; set; }
    public long FirstSeen { get; set; }
    public long LastSeen { get; set; }

    public bool IsBound => !string.IsNullOrEmpty(OptionId);
}
