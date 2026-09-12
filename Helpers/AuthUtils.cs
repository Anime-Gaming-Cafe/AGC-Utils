#region

using System.Text.Json;
using AGC_Management.Enums.Web;
using IniParser.Model;

#endregion

namespace AGC_Management.Utils;

public sealed class AuthUtils
{
    private static readonly (string ConfigKey, AccessLevel Level)[] RoleMappings =
    [
        ("AdminRoleId", AccessLevel.Administrator),
        ("ModRoleId", AccessLevel.Moderator),
        ("SupportRoleId", AccessLevel.Supporter),
        ("HeadEventmanagerRoleId", AccessLevel.HeadEventmanager),
        ("StaffRoleId", AccessLevel.Team),
        ("EventlerRoleId", AccessLevel.Team)
    ];

    public static Task<AccessLevel> ResolveAccessLevelAsync(ulong userId)
    {
        var guild = CurrentApplication.TargetGuild;
        if (!guild.Members.TryGetValue(userId, out var user))
            return Task.FromResult(AccessLevel.NichtImServer);

        var servercfg = BotConfig.GetConfig()["ServerConfig"];

        if (TryGetRole(guild, servercfg, "WebOverrideRoleId", out var overrideRole) &&
            user.Roles.Contains(overrideRole))
            return Task.FromResult(AccessLevel.Administrator);

        if (user.Id == GlobalProperties.BotOwnerId) return Task.FromResult(AccessLevel.BotOwner);

        foreach (var (key, level) in RoleMappings)
            if (TryGetRole(guild, servercfg, key, out var role) && user.Roles.Contains(role))
                return Task.FromResult(level);

        return Task.FromResult(AccessLevel.User);
    }

    public static async Task<string> RetrieveRole(ulong userId) => (await ResolveAccessLevelAsync(userId)).ToString();

    private static bool TryGetRole(DiscordGuild guild, KeyDataCollection servercfg, string key, out DiscordRole role)
    {
        role = null;
        var value = servercfg[key];
        if (string.IsNullOrEmpty(value) || !ulong.TryParse(value, out var roleId)) return false;

        role = guild.GetRole(roleId);
        return role != null;
    }

    public static async Task<string> RetrieveName(JsonElement userClaims)
    {
        var userId_ = userClaims.GetProperty("id").ToString();
        var userId = ulong.Parse(userId_);

        return (await CurrentApplication.DiscordClient.GetUserAsync(userId)).UsernameWithDiscriminator;
    }

    public static async Task<string> RetrieveDisplayName(ulong userId)
    {
        return (await CurrentApplication.DiscordClient.GetUserAsync(userId)).GlobalName;
    }

    public static async Task<string?> RetrieveDisplayName(JsonElement userClaims)
    {
        var userId_ = userClaims.GetProperty("id").ToString();
        var userId = ulong.Parse(userId_);

        return (await CurrentApplication.DiscordClient.GetUserAsync(userId)).GlobalName;
    }

    public static async Task<string> RetrieveId(JsonElement userClaims)
    {
        var userId_ = userClaims.GetProperty("id").ToString();
        return userId_;
    }
}
