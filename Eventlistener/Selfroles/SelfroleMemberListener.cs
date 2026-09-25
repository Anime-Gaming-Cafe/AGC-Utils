#region

using AGC_Management.Services;

#endregion

namespace AGC_Management.Eventlistener.Selfroles;

/// <summary>
///     Keeps track of who holds which selfrole, so a rejoin does not mean picking everything again. The
///     set is kept current on every role change rather than only at the door, because a role granted or
///     taken away by a teammate has to count the same as one picked in the menu.
/// </summary>
[EventHandler]
public sealed class SelfroleMemberListener : BaseCommandModule
{
    [Event]
    public Task GuildMemberRemoved(DiscordClient client, GuildMemberRemoveEventArgs args)
    {
        if (args.Guild != CurrentApplication.TargetGuild) return Task.CompletedTask;

        _ = Task.Run(async () =>
        {
            try
            {
                await SelfroleService.SaveStickyAsync(args.Member);
            }
            catch (Exception e)
            {
                CurrentApplication.Logger.Error(e, "Selfroles: Merken fuer {User} fehlgeschlagen", args.Member.Id);
            }
        });

        return Task.CompletedTask;
    }

    /// <summary>
    ///     Every role change goes through here, no matter who made it, so a role a teammate hands out by
    ///     hand is remembered exactly like one somebody picked in the menu.
    /// </summary>
    [Event]
    public Task GuildMemberUpdated(DiscordClient client, GuildMemberUpdateEventArgs args)
    {
        if (args.Guild != CurrentApplication.TargetGuild || args.Member.IsBot) return Task.CompletedTask;

        var before = args.RolesBefore?.Select(role => role.Id).ToHashSet() ?? [];
        var after = args.RolesAfter?.Select(role => role.Id).ToHashSet() ?? [];

        var added = after.Except(before).ToList();
        var removed = before.Except(after).ToList();
        if (added.Count == 0 && removed.Count == 0) return Task.CompletedTask;

        _ = Task.Run(async () =>
        {
            try
            {
                await SelfroleService.SyncMemberRolesAsync(args.Member.Id, added, removed);
            }
            catch (Exception e)
            {
                CurrentApplication.Logger.Error(e, "Selfroles: Abgleich fuer {User} fehlgeschlagen", args.Member.Id);
            }
        });

        return Task.CompletedTask;
    }

    [Event]
    public Task GuildMemberAdded(DiscordClient client, GuildMemberAddEventArgs args)
    {
        if (args.Guild != CurrentApplication.TargetGuild) return Task.CompletedTask;

        _ = Task.Run(async () =>
        {
            try
            {
                await SelfroleService.RestoreStickyAsync(args.Member);
            }
            catch (Exception e)
            {
                CurrentApplication.Logger.Error(e, "Selfroles: Wiederherstellen fuer {User} fehlgeschlagen",
                    args.Member.Id);
            }
        });

        return Task.CompletedTask;
    }
}
