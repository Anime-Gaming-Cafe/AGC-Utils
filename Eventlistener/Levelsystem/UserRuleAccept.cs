#region

using AGC_Management.Utils;

#endregion

namespace AGC_Management.Eventlistener.Levelsystem;

[EventHandler]
public class UserRuleAccept : BaseCommandModule
{
    [Event]
    private Task GuildMemberUpdated(DiscordClient client, GuildMemberUpdateEventArgs args)
    {
        if (args.Guild == null) return Task.CompletedTask;

        if (args.Member.IsBot) return Task.CompletedTask;

        _ = Task.Run(async () =>
        {
            if (CurrentApplication.TargetGuild == null)
                return;

            if (args.Guild != CurrentApplication.TargetGuild) return;

            if (args.PendingBefore == args.PendingAfter) return;

            if (args.PendingBefore == true && args.PendingAfter == false)
            {
                var user = args.Member;
                await LevelUtils.AddUserToDbIfNot(user);
                await LevelUtils.RestoreRoles(user);
            }
        });
        return Task.CompletedTask;
    }
}