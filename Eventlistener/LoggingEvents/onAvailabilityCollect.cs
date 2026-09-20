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

    /// <summary>
    ///     Joining, leaving, switching, self-muting, camera and stream are all the member acting. A
    ///     moderator server-muting someone who is AFK is not, so that case is filtered out - otherwise
    ///     the mute alone would mark them reachable and earn them a ping.
    /// </summary>
    [Event]
    public Task VoiceStateUpdated(DiscordClient client, VoiceStateUpdateEventArgs args)
    {
        if (args.Guild is null || args.Guild.Id != CurrentApplication.TargetGuild?.Id) return Task.CompletedTask;

        var member = args.After?.Member ?? args.Before?.Member;
        if (member is null || member.IsBot) return Task.CompletedTask;

        if (IsServerSideOnly(args.Before, args.After)) return Task.CompletedTask;

        AvailabilityService.Touch(member.Id, AvailabilitySignal.Voice);
        return Task.CompletedTask;
    }

    private static bool IsServerSideOnly(DiscordVoiceState? before, DiscordVoiceState? after)
    {
        if (before is null || after is null) return false;
        if (before.Channel?.Id != after.Channel?.Id) return false;
        if (before.IsSelfMuted != after.IsSelfMuted || before.IsSelfDeafened != after.IsSelfDeafened) return false;
        if (before.IsSelfStream != after.IsSelfStream || before.IsSelfVideo != after.IsSelfVideo) return false;

        return before.IsServerMuted != after.IsServerMuted || before.IsServerDeafened != after.IsServerDeafened;
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
