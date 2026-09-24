#region

using AGC_Management.Services;

#endregion

namespace AGC_Management.Eventlistener.Autoposts;

[EventHandler]
public sealed class AutopostMemberListener : BaseCommandModule
{
    [Event]
    public Task GuildMemberAdded(DiscordClient client, GuildMemberAddEventArgs args)
    {
        if (IsTargetGuild(args.Guild) && args.Member is { IsBot: false }) Adjust(1);
        return Task.CompletedTask;
    }

    [Event]
    public Task GuildMemberRemoved(DiscordClient client, GuildMemberRemoveEventArgs args)
    {
        if (IsTargetGuild(args.Guild) && args.Member is { IsBot: false }) Adjust(-1);
        return Task.CompletedTask;
    }

    private static bool IsTargetGuild(DiscordGuild? guild)
    {
        return guild != null && CurrentApplication.TargetGuild != null && guild.Id == CurrentApplication.TargetGuild.Id;
    }

    private static void Adjust(int delta)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await AutopostService.AdjustJoinCountAsync(delta);
            }
            catch (Exception e)
            {
                CurrentApplication.Logger.Error(e, "Autoposts: join counter update failed");
            }
        });
    }
}
