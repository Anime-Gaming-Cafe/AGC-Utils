#region

using AGC_Management.Services;
using AGC_Management.Utils;

#endregion

namespace AGC_Management.Eventlistener.Entbannung;

[EventHandler]
public class EntbannungMemberListener : BaseCommandModule
{
    [Event]
    public static Task GuildMemberAdded(DiscordClient client, GuildMemberAddEventArgs args)
    {
        if (!EntbannungConfig.IsUnbanGuild(args.Guild)) return Task.CompletedTask;

        _ = Task.Run(async () =>
        {
            try
            {
                if (await EntbannungService.GetSperreAsync(args.Member.Id) is null) return;
                await args.Member.GrantRoleAsync(args.Guild.GetRole(EntbannungConfig.SperreRoleId),
                    "Nutzer ist gesperrt.");
            }
            catch (Exception e)
            {
                await ErrorReporting.SendErrorToDev(client, args.Member, e);
            }
        });
        return Task.CompletedTask;
    }

    [Event]
    public static Task GuildMemberUpdated(DiscordClient client, GuildMemberUpdateEventArgs args)
    {
        if (!EntbannungConfig.Active || !DiscordBotService.IsReady) return Task.CompletedTask;
        if (args.Guild is null || args.Guild.Id != CurrentApplication.TargetGuild?.Id) return Task.CompletedTask;

        var teamRoleId = EntbannungConfig.MainGuildTeamRoleId;
        var hadRole = args.RolesBefore.Any(r => r.Id == teamRoleId);
        var hasRole = args.RolesAfter.Any(r => r.Id == teamRoleId);
        if (hadRole == hasRole) return Task.CompletedTask;

        var ignored = EntbannungConfig.SyncIgnoredRoleIds;
        if (args.RolesAfter.Any(r => ignored.Contains(r.Id))) return Task.CompletedTask;

        _ = Task.Run(async () =>
        {
            try
            {
                var unbanGuild = await EntbannungService.GetUnbanGuildAsync(client);
                if (!unbanGuild.Members.TryGetValue(args.Member.Id, out var unbanMember)) return;

                var unbanTeamRole = unbanGuild.GetRole(EntbannungConfig.TeamRoleId);
                var pingRole = unbanGuild.GetRole(EntbannungConfig.PingRoleId);
                if (hasRole)
                {
                    await unbanMember.GrantRoleAsync(unbanTeamRole, "Teamrolle auf dem Hauptserver erhalten.");
                    await unbanMember.GrantRoleAsync(pingRole, "Teamrolle auf dem Hauptserver erhalten.");
                }
                else
                {
                    await unbanMember.RevokeRoleAsync(unbanTeamRole, "Teamrolle auf dem Hauptserver verloren.");
                    await unbanMember.RevokeRoleAsync(pingRole, "Teamrolle auf dem Hauptserver verloren.");
                }
            }
            catch (Exception e)
            {
                await ErrorReporting.SendErrorToDev(client, args.Member, e);
            }
        });
        return Task.CompletedTask;
    }
}
