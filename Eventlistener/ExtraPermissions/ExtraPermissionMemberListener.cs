#region

using System.Collections.Concurrent;
using AGC_Management.Enums.ExtraPermissions;
using AGC_Management.Services;

#endregion

namespace AGC_Management.Eventlistener.ExtraPermissions;

[EventHandler]
public sealed class ExtraPermissionMemberListener : BaseCommandModule
{
    private static readonly ConcurrentDictionary<ulong, long> VoiceDebounce = new();
    private const int VoiceDebounceSeconds = 5;

    [Event]
    public Task GuildMemberAdded(DiscordClient client, GuildMemberAddEventArgs args)
    {
        if (!IsTargetGuild(args.Guild) || args.Member == null || args.Member.IsBot) return Task.CompletedTask;

        Evaluate(args.Member);
        return Task.CompletedTask;
    }

    [Event]
    public Task GuildMemberUpdated(DiscordClient client, GuildMemberUpdateEventArgs args)
    {
        if (!IsTargetGuild(args.Guild) || args.Member == null || args.Member.IsBot) return Task.CompletedTask;

        Evaluate(args.Member, ExtraPermissionConditionType.Boost, ExtraPermissionConditionType.Role);
        return Task.CompletedTask;
    }

    [Event]
    public Task VoiceStateUpdated(DiscordClient client, VoiceStateUpdateEventArgs args)
    {
        if (!IsTargetGuild(args.Guild)) return Task.CompletedTask;

        var member = args.After?.Member ?? args.Before?.Member;
        if (member == null || member.IsBot) return Task.CompletedTask;

        // A channel hop leaves "is in voice" unchanged, so there is nothing to re-decide.
        if (args.Before?.Channel != null && args.After?.Channel != null) return Task.CompletedTask;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var last = VoiceDebounce.GetOrAdd(member.Id, 0);
        if (now - last < VoiceDebounceSeconds) return Task.CompletedTask;

        VoiceDebounce[member.Id] = now;
        PruneVoiceDebounce(now);

        Evaluate(member, ExtraPermissionConditionType.Voice);
        return Task.CompletedTask;
    }

    private static void PruneVoiceDebounce(long now)
    {
        if (VoiceDebounce.Count < 1000) return;

        foreach (var entry in VoiceDebounce)
            if (now - entry.Value >= VoiceDebounceSeconds)
                VoiceDebounce.TryRemove(entry.Key, out _);
    }

    private static bool IsTargetGuild(DiscordGuild? guild)
    {
        return guild != null && CurrentApplication.TargetGuild != null && guild.Id == CurrentApplication.TargetGuild.Id;
    }

    private static void Evaluate(DiscordMember member, params ExtraPermissionConditionType[] triggers)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await ExtraPermissionService.EvaluateMemberAsync(member, triggers);
            }
            catch (Exception e)
            {
                CurrentApplication.Logger.Error(e,
                    "ExtraPermissions: evaluation failed for member {MemberId}", member.Id);
            }
        });
    }
}
