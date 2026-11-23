namespace AGC_Management.Utils;

public static class EntbannungConfig
{
    private const string Section = "Entbannung";

    public static bool Active => bool.TryParse(Get("Active"), out var active) && active;
    public static ulong ServerId => GetId("ServerId");
    public static ulong TeamRoleId => GetId("TeamRoleId");
    public static ulong MainGuildTeamRoleId => GetId("MainGuildTeamRoleId");
    public static ulong PingRoleId => GetId("PingRoleId");
    public static ulong AppealRoleId => GetId("AppealRoleId");
    public static ulong SperreRoleId => GetId("SperreRoleId");
    public static ulong LogChannelId => GetId("LogChannelId");
    public static ulong HistoryChannelId => GetId("HistoryChannelId");
    public static ulong AbstimmungsChannelId => GetId("AbstimmungsChannelId");
    public static ulong AbstimmungsLogChannelId => GetId("AbstimmungsLogChannelId");
    public static ulong VoteCategoryId => GetId("VoteCategoryChannelId");
    public static ulong SperreInfoChannelId => GetId("SperreInfoChannelId");
    public static ulong BearbeitetCategoryId => GetId("BearbeitetCategoryId");

    public static IReadOnlyList<ulong> SyncIgnoredRoleIds =>
        (Get("SyncIgnoredRoleList") ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(x => ulong.TryParse(x, out var id) ? id : 0)
        .Where(id => id != 0)
        .ToList();

    public static bool IsUnbanGuild(DiscordGuild? guild)
    {
        return guild is not null && Active && guild.Id == ServerId;
    }

    private static string? Get(string key)
    {
        try
        {
            return BotConfig.GetConfig()[Section][key];
        }
        catch
        {
            return null;
        }
    }

    private static ulong GetId(string key)
    {
        return ulong.TryParse(Get(key), out var id) ? id : 0;
    }
}
