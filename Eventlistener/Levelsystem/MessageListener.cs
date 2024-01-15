﻿using AGC_Management.Entities;
using AGC_Management.Utils;
using DisCatSharp.ApplicationCommands;

namespace AGC_Management.Eventlistener.Levelsystem;

[EventHandler]
public sealed class MessageListener : ApplicationCommandsModule
{
    
    [Event]
    private Task MessageCreated(DiscordClient client, MessageCreateEventArgs args)
    {
        if (args.Author.IsBot)
        {
            return Task.CompletedTask;
        }
        _ = Task.Run(async () =>
        {
            if (CurrentApplication.TargetGuild == null) // check init
            {
                return;
            }
            if (args.Channel.Type == ChannelType.Private || args.Author.IsBot)
                return;
            if (args.Guild.Id != CurrentApplication.TargetGuild.Id)
            {
                return;
            }
            Console.WriteLine("Trying to give xp");
            await LevelUtils.GiveXP(args.Author, LevelUtils.GetBaseXp(XpRewardType.Message), XpRewardType.Message);
        });
        return Task.CompletedTask;
    }
    
}