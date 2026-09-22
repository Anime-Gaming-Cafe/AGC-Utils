#region

#endregion

namespace AGC_Management.Utils;

public static class TeamChecker
{
    /// <summary>
    ///     The one global ticket team role from config.ini. Still the fallback for categories that have no
    ///     handler roles of their own, so it must not throw when the key is missing.
    /// </summary>
    public static bool IsSupporter(DiscordMember member)
    {
        ulong supporterRole;
        try
        {
            supporterRole = ulong.Parse(BotConfig.GetConfig()["TicketConfig"]["TeamRoleId"]);
        }
        catch (Exception)
        {
            return false;
        }

        return supporterRole != 0 && member.Roles.Any(x => x.Id == supporterRole);
    }
}
