#region

using AGC_Management.Services;

#endregion

namespace AGC_Management.Eventlistener;

/// <summary>
///     GUILD_CREATE wipes the guild's member cache. That happens on startup and after every
///     reconnect, so the cache is rebuilt at exactly that point.
/// </summary>
[EventHandler]
public class MemberCacheListener : BaseCommandModule
{
    [Event]
    public static Task GuildAvailable(DiscordClient client, GuildCreateEventArgs args)
    {
        if (args.Guild.Id != MemberCacheService.TargetGuildId) return Task.CompletedTask;

        _ = MemberCacheService.RefreshAsync(client, args.Guild.Id, "guild available");
        return Task.CompletedTask;
    }

    [Event]
    public static Task GuildCreated(DiscordClient client, GuildCreateEventArgs args)
        => GuildAvailable(client, args);

    /// <summary>
    ///     A member that was not cached yet lands in the cache through this event, but only half
    ///     populated. Complete the entry so JoinedAt and PremiumSince are not left blank.
    /// </summary>
    [Event]
    public static Task GuildMemberUpdated(DiscordClient client, GuildMemberUpdateEventArgs args)
    {
        if (args.Guild is null || args.Guild.Id != MemberCacheService.TargetGuildId) return Task.CompletedTask;

        _ = MemberCacheService.EnsureCompleteAsync(args.Guild, args.Member);
        return Task.CompletedTask;
    }
}
