namespace AGC_Management.Utils;

internal static class DiscordExtension
{
    internal static bool isTeamMember(this DiscordMember member)
    {
        var teamRole = ulong.Parse(BotConfig.GetConfig()["ServerConfig"]["StaffRoleId"]);
        return member.Roles.Any(x => x.Id == teamRole);
    }

    internal static string GetFormattedUserName(this DiscordUser member)
    {
        if (member.IsMigrated) return member.Username;

        return member.Username + "#" + member.Discriminator;
    }

    internal static string GetFormattedUserName(this DiscordMember member)
    {
        if (member.IsMigrated) return member.Username;

#pragma warning disable DCS0102 // [Discord] Deprecated
		return member.Username + "#" + member.Discriminator;
#pragma warning restore DCS0102 // [Discord] Deprecated
	}
}