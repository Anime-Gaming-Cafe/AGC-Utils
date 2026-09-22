#region

using AGC_Management.Entities.Ticket;
using AGC_Management.Services;

#endregion

namespace AGC_Management.Utils;

/// <summary>
///     The single place that answers "may this member work on this ticket". Categories carry their own
///     handler roles; a category that has none falls back to the one global ticket team role, so a
///     server that never configures anything keeps the behaviour it had before categories existed.
/// </summary>
public static class TicketAccess
{
    public static async Task<IReadOnlyList<ulong>> GlobalAccessRoleIdsAsync()
    {
        var raw = await RuntimeSettings.GetAsync(TicketCategoryService.SettingsSection, "GlobalAccessRoleIds");
        if (string.IsNullOrWhiteSpace(raw)) return [];

        return
        [
            .. raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part => ulong.TryParse(part, out var id) ? id : 0)
                .Where(id => id > 0)
                .Distinct()
        ];
    }

    public static async Task<bool> MayHandleAsync(DiscordMember? member, TicketCategory? category)
    {
        if (member is null) return false;

        var global = await GlobalAccessRoleIdsAsync();
        if (global.Count > 0 && member.Roles.Any(role => global.Contains(role.Id))) return true;

        if (category is null || category.HandlerRoleIds.Count == 0) return TeamChecker.IsSupporter(member);

        return member.Roles.Any(role => category.HandlerRoleIds.Contains(role.Id));
    }

    public static async Task<bool> MayHandleAsync(DiscordMember? member, DiscordChannel channel)
    {
        if (member is null) return false;
        var category = await TicketCategoryService.GetForChannelAsync(channel.Id);
        return await MayHandleAsync(member, category);
    }

    public static async Task<bool> MayHandleAsync(DiscordUser user, DiscordGuild guild, DiscordChannel channel)
    {
        var member = await user.ConvertToMember(guild);
        return await MayHandleAsync(member, channel);
    }

    /// <summary>Categories this member may work on. Drives the dashboard queue and the transfer menu.</summary>
    public static async Task<List<TicketCategory>> VisibleCategoriesAsync(DiscordMember? member,
        bool includeDisabled = true)
    {
        if (member is null) return [];

        var categories = await TicketCategoryService.GetAllAsync(includeDisabled);
        var visible = new List<TicketCategory>();
        foreach (var category in categories)
            if (await MayHandleAsync(member, category))
                visible.Add(category);

        return visible;
    }

    /// <summary>Same as above for the dashboard, which only knows the user id.</summary>
    public static async Task<List<TicketCategory>> VisibleCategoriesAsync(ulong userId, bool includeDisabled = true)
    {
        var member = await ResolveMemberAsync(userId);
        return await VisibleCategoriesAsync(member, includeDisabled);
    }

    private static async Task<DiscordMember?> ResolveMemberAsync(ulong userId)
    {
        var guild = CurrentApplication.TargetGuild;
        if (guild is null || userId == 0) return null;

        if (guild.Members.TryGetValue(userId, out var cached)) return cached;

        try
        {
            return await guild.GetMemberAsync(userId);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
