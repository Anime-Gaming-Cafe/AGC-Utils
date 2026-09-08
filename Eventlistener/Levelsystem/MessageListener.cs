#region

using AGC_Management.Entities;
using AGC_Management.Utils;
using DisCatSharp.ApplicationCommands;

#endregion

namespace AGC_Management.Eventlistener.Levelsystem;

[EventHandler]
public sealed class MessageListener : ApplicationCommandsModule
{
    [Event]
    private Task MessageCreated(DiscordClient client, MessageCreateEventArgs args)
    {
        if (args.Author.IsBot) return Task.CompletedTask;

        _ = Task.Run(async () =>
        {
            if (CurrentApplication.TargetGuild == null) // check init
                return;
            if (args.Guild == null) return;

            if (args.Channel.Type == ChannelType.Private || args.Author.IsBot)
                return;
            if (args.Guild.Id != CurrentApplication.TargetGuild.Id) return;

            if (await LevelUtils.IsChannelBlocked(args.Channel.Id)) return;

            CurrentApplication.Logger.Debug("Trying to handout xp to user " + args.Author.Username);
            await LevelUtils.GiveXP(args.Author, LevelUtils.GetBaseXp(XpRewardType.Message), XpRewardType.Message);
        });
        return Task.CompletedTask;
    }
}