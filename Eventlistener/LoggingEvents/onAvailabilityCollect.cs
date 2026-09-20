#region

using AGC_Management.Enums;
using AGC_Management.Services;
using DisCatSharp.ApplicationCommands;

#endregion

namespace AGC_Management.Eventlistener.LoggingEvents;

[EventHandler]
public class onAvailabilityCollect : ApplicationCommandsModule
{
    [Event]
    public Task MessageCreated(DiscordClient client, MessageCreateEventArgs args)
    {
        if (args.Guild is null || args.Guild.Id != CurrentApplication.TargetGuild?.Id) return Task.CompletedTask;
        if (args.Message.Author.IsBot) return Task.CompletedTask;

        AvailabilityService.Touch(args.Message.Author.Id, AvailabilitySignal.Message);
        return Task.CompletedTask;
    }

    [Event]
    public Task TypingStarted(DiscordClient client, TypingStartEventArgs args)
    {
        if (args.Guild is null || args.Guild.Id != CurrentApplication.TargetGuild?.Id) return Task.CompletedTask;
        if (args.User.IsBot) return Task.CompletedTask;

        AvailabilityService.Touch(args.User.Id, AvailabilitySignal.Typing);
        return Task.CompletedTask;
    }
}
