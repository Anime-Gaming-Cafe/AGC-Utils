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
}
