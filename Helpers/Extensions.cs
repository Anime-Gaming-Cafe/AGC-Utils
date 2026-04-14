namespace AGC_Management.Utils;

internal static class DiscordExtension
{
    internal static bool isTeamMember(this DiscordMember member)
    {
        var teamRole = ulong.Parse(BotConfig.GetConfig()["ServerConfig"]["StaffRoleId"]);
        return member.Roles.Any(x => x.Id == teamRole);
    }

    /// <summary>
    ///     Formats the username of the given Discord member.
    /// </summary>
    /// <param name="member">The DiscordUser object for which to format the username.</param>
    /// <returns>
    ///     The formatted username of the given Discord member.
    ///     If the member is migrated, returns the username only.
    ///     If the member is not migrated, returns the username concatenated with the discriminator.
    /// </returns>
    internal static string GetFormattedUserName(this DiscordUser member)
    {
        // if migrated
        if (member.IsMigrated) return member.Username;
        // if not migrated

        return member.Username + "#" + member.Discriminator;
    }

    /// <summary>
    ///     Formats the username of the given Discord member.
    /// </summary>
    /// <param name="member">The DiscordMember object for which to format the username.</param>
    /// <returns>
    ///     The formatted username of the given Discord member.
    ///     If the member is migrated, returns the username only.
    ///     If the member is not migrated, returns the username concatenated with the discriminator.
    /// </returns>
    internal static string GetFormattedUserName(this DiscordMember member)
    {
        // if migrated
        if (member.IsMigrated) return member.Username;
		// if not migrated

#pragma warning disable DCS0102 // [Discord] Deprecated
		return member.Username + "#" + member.Discriminator;
#pragma warning restore DCS0102 // [Discord] Deprecated
	}
}