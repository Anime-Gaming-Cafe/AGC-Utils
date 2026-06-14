#region

using AGC_Management.Services;

#endregion

namespace AGC_Management.Eventlistener.BoosterColors;

[EventHandler]
public sealed class BoosterColorMemberUpdateListener : BaseCommandModule
{
    [Event]
    public Task GuildMemberUpdated(DiscordClient client, GuildMemberUpdateEventArgs args)
    {
        if (args.Guild == null) return Task.CompletedTask;
        if (args.Member == null || args.Member.IsBot) return Task.CompletedTask;

        _ = Task.Run(async () =>
        {
            if (CurrentApplication.TargetGuild == null) return;
            if (args.Guild != CurrentApplication.TargetGuild) return;

            try
            {
                // Removes any color role from the member if they stopped boosting / lost the donate role.
                await BoosterColorService.CleanupMemberAsync(args.Member);
            }
            catch (Exception e)
            {
                CurrentApplication.Logger.Error(e, "BoosterColors: cleanup on member update failed");
            }
        });

        return Task.CompletedTask;
    }
}
