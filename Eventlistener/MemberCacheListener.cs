#region

using AGC_Management.Services;

#endregion

namespace AGC_Management.Eventlistener;

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
