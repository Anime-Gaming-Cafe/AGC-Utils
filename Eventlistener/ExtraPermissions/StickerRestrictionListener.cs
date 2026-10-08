#region

using System.Collections.Concurrent;
using AGC_Management.Entities.ExtraPermissions;
using AGC_Management.Enums.ExtraPermissions;
using AGC_Management.Services;
using DisCatSharp.Exceptions;

#endregion

namespace AGC_Management.Eventlistener.ExtraPermissions;

[EventHandler]
public sealed class StickerRestrictionListener : BaseCommandModule
{
    private static readonly ConcurrentDictionary<ulong, long> DmCooldown = new();
    private const int DmCooldownSeconds = 600;

    [Event]
    public Task MessageCreated(DiscordClient client, MessageCreateEventArgs args)
    {
        if (args.Author.IsBot || args.Guild == null || args.Message.Stickers.Count == 0) return Task.CompletedTask;
        if (CurrentApplication.TargetGuild == null || args.Guild.Id != CurrentApplication.TargetGuild.Id)
            return Task.CompletedTask;

        _ = Task.Run(async () =>
        {
            try
            {
                var member = args.Author as DiscordMember ?? await args.Guild.GetMemberAsync(args.Author.Id);
                var permission = await ExtraPermissionService.GetStickerBlockingPermissionAsync(member);
                if (permission == null) return;

                await args.Message.DeleteAsync($"ExtraPermission: Sticker ohne {permission.PermName}");
                await NotifyAsync(member, permission);
            }
            catch (Exception e) when (e is NotFoundException or UnauthorizedException)
            {
            }
            catch (Exception e)
            {
                CurrentApplication.Logger.Error(e,
                    "StickerRestriction: failed to handle message {MessageId}", args.Message.Id);
            }
        });

        return Task.CompletedTask;
    }

    private static async Task NotifyAsync(DiscordMember member, ExtraPermission permission)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (DmCooldown.TryGetValue(member.Id, out var last) && now - last < DmCooldownSeconds) return;

        DmCooldown[member.Id] = now;
        PruneDmCooldown(now);

        var state = await ExtraPermissionService.GetMemberStateAsync(member.Id, permission.PermName);
        var reason = state.EffectiveState == ExtraPermissionState.Revoked
            ? $"dir die **{permission.DisplayName}** entzogen wurde"
            : $"du noch keine Sticker senden darfst. Dafür benötigst du die **{permission.DisplayName}**";

        try
        {
            await member.SendMessageAsync($"Deine Nachricht wurde gelöscht, da {reason}.");
        }
        catch
        {
        }
    }

    private static void PruneDmCooldown(long now)
    {
        if (DmCooldown.Count < 1000) return;

        foreach (var entry in DmCooldown)
            if (now - entry.Value >= DmCooldownSeconds)
                DmCooldown.TryRemove(entry.Key, out _);
    }
}
