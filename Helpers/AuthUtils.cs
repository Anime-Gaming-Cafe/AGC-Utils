#region

using System.Text.Json;
using AGC_Management.Enums.Web;
using DisCatSharp.Exceptions;

#endregion

namespace AGC_Management.Utils;

public sealed class AuthUtils
{
    public static async Task<string> RetrieveRole(ulong userId)
    {
        var guild = CurrentApplication.TargetGuild;
        DiscordMember? user = null;
        var servercfg = BotConfig.GetConfig()["ServerConfig"];
        var adminRole = guild.GetRole(ulong.Parse(servercfg["AdminRoleId"]));
        DiscordRole? overrideRole = null;
        try
        {
            overrideRole = guild.GetRole(ulong.Parse(servercfg["WebOverrideRoleId"]));
        }
        catch (Exception)
        {
            // ignored
        }

        var supRole = guild.GetRole(ulong.Parse(servercfg["SupportRoleId"]));
        var modRole = guild.GetRole(ulong.Parse(servercfg["ModRoleId"]));
        var staffRole = guild.GetRole(ulong.Parse(servercfg["StaffRoleId"]));
        try
        {
            user = CurrentApplication.TargetGuild.Members.Values.First(x => x.Id == userId);
            if (user == null) return AccessLevel.NichtImServer.ToString();
        }
        catch (NotFoundException)
        {
            return AccessLevel.NichtImServer.ToString();
        }
        catch (InvalidOperationException)
        {
            return AccessLevel.NichtImServer.ToString();
        }

        try
        {
            if (overrideRole != null && user.Roles.Contains(overrideRole)) return AccessLevel.Administrator.ToString();
        }
        catch (Exception)
        {
            // ignored
        }

        // bot owner
        if (user.Id == GlobalProperties.BotOwnerId) return AccessLevel.BotOwner.ToString();

        // admin
        if (user.Roles.Contains(adminRole)) return AccessLevel.Administrator.ToString();

        try
        {
            ulong headmodroleid = 817732206876688387;
            var headmodrole = guild.GetRole(headmodroleid);
            if (user.Roles.Contains(headmodrole)) return AccessLevel.HeadModerator.ToString();
        }
        catch (Exception)
        {
            // ignored
        }

        // mod
        if (user.Roles.Contains(modRole)) return AccessLevel.Moderator.ToString();

        // sup
        if (user.Roles.Contains(supRole)) return AccessLevel.Supporter.ToString();

        var eventmanagerRole = guild.Roles.Values.FirstOrDefault(x => x.Id.Equals(1157337266960732232));
        if (eventmanagerRole != null && user.Roles.Contains(eventmanagerRole))
            return AccessLevel.HeadEventmanager.ToString();

        // staff
        if (user.Roles.Contains(staffRole)) return AccessLevel.Team.ToString();

        // user
        return AccessLevel.User.ToString();
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

    public static async Task<string> RetrieveRole(JsonElement userClaims)
    {
        var userId_ = userClaims.GetProperty("id").ToString();
        var userId = ulong.Parse(userId_);


        var guild = CurrentApplication.TargetGuild;
        DiscordMember? user = null;
        var servercfg = BotConfig.GetConfig()["ServerConfig"];
        var adminRole = guild.GetRole(ulong.Parse(servercfg["AdminRoleId"]));
        var supRole = guild.GetRole(ulong.Parse(servercfg["SupportRoleId"]));
        DiscordRole? overrideRole = null;
        try
        {
            overrideRole = guild.GetRole(ulong.Parse(servercfg["WebOverrideRoleId"]));
        }
        catch (Exception)
        {
            // ignored
        }

        var modRole = guild.GetRole(ulong.Parse(servercfg["ModRoleId"]));
        var staffRole = guild.GetRole(ulong.Parse(servercfg["StaffRoleId"]));
        try
        {
            user = CurrentApplication.TargetGuild.Members.Values.First(x => x.Id == userId);
            if (user == null) return AccessLevel.NichtImServer.ToString();
        }
        catch (NotFoundException)
        {
            return AccessLevel.NichtImServer.ToString();
        }
        catch (InvalidOperationException)
        {
            return AccessLevel.NichtImServer.ToString();
        }

        try
        {
            if (overrideRole != null && user.Roles.Contains(overrideRole)) return AccessLevel.Administrator.ToString();
        }
        catch (Exception)
        {
            // ignored
        }

        // bot owner
        if (user.Id == GlobalProperties.BotOwnerId) return AccessLevel.BotOwner.ToString();

        // admin
        if (user.Roles.Contains(adminRole)) return AccessLevel.Administrator.ToString();


        try
        {
            ulong headmodroleid = 817732206876688387;
            var headmodrole = guild.GetRole(headmodroleid);
            if (user.Roles.Contains(headmodrole)) return AccessLevel.HeadModerator.ToString();
        }
        catch (Exception)
        {
            // ignored
        }

        // mod
        if (user.Roles.Contains(modRole)) return AccessLevel.Moderator.ToString();

        // sup
        if (user.Roles.Contains(supRole)) return AccessLevel.Supporter.ToString();

        var eventmanagerRole = guild.Roles.Values.FirstOrDefault(x => x.Id.Equals(1157337266960732232));
        if (eventmanagerRole != null && user.Roles.Contains(eventmanagerRole))
            return AccessLevel.HeadEventmanager.ToString();

        // staff
        if (user.Roles.Contains(staffRole)) return AccessLevel.Team.ToString();

        // user
        return AccessLevel.User.ToString();
    }


    public static async Task<List<string>> RetrieveRoles(ulong id)
    {
        var userId = ulong.Parse(id.ToString());

        var guild = CurrentApplication.TargetGuild;
        DiscordMember? user = null;
        var servercfg = BotConfig.GetConfig()["ServerConfig"];
        var adminRole = guild.GetRole(ulong.Parse(servercfg["AdminRoleId"]));
        var supRole = guild.GetRole(ulong.Parse(servercfg["SupportRoleId"]));
        DiscordRole? overrideRole = null;

        try
        {
            overrideRole = guild.GetRole(ulong.Parse(servercfg["WebOverrideRoleId"]));
        }
        catch (Exception)
        {
            // ignored
        }

        var modRole = guild.GetRole(ulong.Parse(servercfg["ModRoleId"]));
        var staffRole = guild.GetRole(ulong.Parse(servercfg["StaffRoleId"]));

        try
        {
            user = await guild.GetMemberAsync(userId);
        }
        catch (NotFoundException)
        {
            return new List<string> { AccessLevel.NichtImServer.ToString() };
        }

        var userRoles = new List<string>();

        // bot owner
        if (user.Id == GlobalProperties.BotOwnerId) userRoles.Add(AccessLevel.BotOwner.ToString());

        // override
        if (overrideRole != null && user.Roles.Contains(overrideRole))
            userRoles.Add(AccessLevel.Administrator.ToString());

        // admin
        if (user.Roles.Contains(adminRole)) userRoles.Add(AccessLevel.Administrator.ToString());

        // mod
        if (user.Roles.Contains(modRole)) userRoles.Add(AccessLevel.Moderator.ToString());

        // sup
        if (user.Roles.Contains(supRole)) userRoles.Add(AccessLevel.Supporter.ToString());

        // staff
        if (user.Roles.Contains(staffRole)) userRoles.Add(AccessLevel.Team.ToString());

        // user
        if (userRoles.Count == 0) userRoles.Add(AccessLevel.User.ToString());

        return userRoles;
    }
}