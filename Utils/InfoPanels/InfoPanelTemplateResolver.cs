#region

using AGC_Management.Helper;

#endregion

namespace AGC_Management.Utils;

/// <summary>
///     Fills the placeholders of an info panel page. Same shape as <see cref="SnippetTemplateResolver" />,
///     including the BuiltIn list, so the dashboard renders the available placeholders from here instead
///     of repeating them in a hint text.
/// </summary>
public static class InfoPanelTemplateResolver
{
    public static readonly (string Token, string Description)[] BuiltIn =
    [
        ("{membercount}", "Aktuelle Mitgliederzahl des Servers."),
        ("{boostcount}", "Anzahl der aktiven Boosts."),
        ("{guildname}", "Name des Servers."),
        ("{unixtimestamp}", "Aktuelle Zeit als Unix-Zeitstempel."),
        ("{unixtimestamp+3600}", "Zeit in einer Stunde. Beliebige Sekundenzahl möglich. Für einen echten Discord-Zeitstempel: <t:{unixtimestamp+3600}:R>")
    ];

    public static string Resolve(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        var guild = CurrentApplication.TargetGuild;
        if (guild is not null)
        {
            text = text.Replace("{membercount}", (guild.MemberCount ?? 0).ToString("N0"));
            text = text.Replace("{boostcount}", (guild.PremiumSubscriptionCount ?? 0).ToString("N0"));
            text = text.Replace("{guildname}", guild.Name);
        }

        return SnippetManagerHelper.FormatStringWithVariables(text);
    }
}
