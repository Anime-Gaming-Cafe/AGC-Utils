namespace AGC_Management.Entities.Ticket;

/// <summary>
///     One selectable ticket kind. Everything that used to be a branch on <c>TicketType</c> lives here
///     as data: which team handles it, where its channel is created, what the user is greeted with.
/// </summary>
public class TicketCategory
{
    /// <summary>Stable key. It is what <c>ticketstore.tickettype</c> holds and what button ids carry.</summary>
    public string CustomId { get; set; } = "";

    public string Label { get; set; } = "";
    public string Description { get; set; } = "";
    public string? Emoji { get; set; }

    /// <summary>Channel name prefix, so a ticket becomes <c>{prefix}-{number}</c>.</summary>
    public string ChannelPrefix { get; set; } = "";

    /// <summary>0 means "use TicketConfig.SupportCategoryId".</summary>
    public ulong DiscordCategoryId { get; set; }

    /// <summary>Roles that may see and work these tickets.</summary>
    public List<ulong> HandlerRoleIds { get; set; } = [];

    /// <summary>Roles pinged in the ticket header. Empty falls back to <see cref="HandlerRoleIds" />.</summary>
    public List<ulong> PingRoleIds { get; set; } = [];

    public string WelcomeText { get; set; } = "";
    public bool IntakeEnabled { get; set; }
    public int MaxOpenPerUser { get; set; } = 1;

    public bool AutoCloseEnabled { get; set; }
    public int AutoCloseReminderHours { get; set; }
    public int AutoCloseHours { get; set; }

    public int SortOrder { get; set; }
    public bool Enabled { get; set; } = true;

    public List<TicketCategoryQuestion> Questions { get; set; } = [];

    public IReadOnlyList<ulong> EffectivePingRoleIds => PingRoleIds.Count > 0 ? PingRoleIds : HandlerRoleIds;
}
