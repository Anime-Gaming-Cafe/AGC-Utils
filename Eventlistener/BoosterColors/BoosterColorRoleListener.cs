#region

using AGC_Management.BoosterColors;
using AGC_Management.Services;

#endregion

namespace AGC_Management.Eventlistener.BoosterColors;

/// <summary>
///     Keeps the panel and its circle emojis in step with role changes made anywhere, including Discord's own
///     role settings. The panel queue debounces, so reordering many roles costs a single refresh.
/// </summary>
[EventHandler]
public sealed class BoosterColorRoleListener : BaseCommandModule
{
    [Event]
    public Task GuildRoleCreated(DiscordClient client, GuildRoleCreateEventArgs args)
    {
        QueueIfRelevant(args.Guild, args.Role);
        return Task.CompletedTask;
    }

    [Event]
    public Task GuildRoleUpdated(DiscordClient client, GuildRoleUpdateEventArgs args)
    {
        var before = args.RoleBefore;
        var after = args.RoleAfter;

        var changed = before is null ||
                      before.Name != after.Name ||
                      before.Position != after.Position ||
                      before.Color.Value != after.Color.Value ||
                      before.Colors?.SecondaryColor?.Value != after.Colors?.SecondaryColor?.Value;

        if (changed) QueueIfRelevant(args.Guild, after, before);
        return Task.CompletedTask;
    }

    [Event]
    public Task GuildRoleDeleted(DiscordClient client, GuildRoleDeleteEventArgs args)
    {
        QueueIfRelevant(args.Guild, args.Role);
        return Task.CompletedTask;
    }

    private static void QueueIfRelevant(DiscordGuild? guild, DiscordRole? role, DiscordRole? previous = null)
    {
        if (guild is null || role is null || CurrentApplication.TargetGuild is null) return;
        if (guild.Id != CurrentApplication.TargetGuild.Id) return;

        var relevant = BoosterColorService.IsColorRoleOrBoundary(guild, role) ||
                       (previous is not null && BoosterColorService.IsColorRoleOrBoundary(guild, previous));
        if (!relevant) return;

        try
        {
            BoosterColorPanelCommands.QueueRefreshPanel();
        }
        catch (Exception e)
        {
            CurrentApplication.Logger.Error(e, "BoosterColors: failed to queue panel refresh after a role change");
        }
    }
}
