#region

using System.Text;
using System.Text.RegularExpressions;
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
    /// <summary>Headroom under EmbedDescription/TextDisplayContent left for whatever text surrounds the list.</summary>
    private const int RoleListBudget = 3500;

    public static readonly (string Token, string Description)[] BuiltIn =
    [
        ("{membercount}", "Aktuelle Mitgliederzahl des Servers."),
        ("{boostcount}", "Anzahl der aktiven Boosts."),
        ("{guildname}", "Name des Servers."),
        ("{unixtimestamp}", "Aktuelle Zeit als Unix-Zeitstempel."),
        ("{unixtimestamp+3600}", "Zeit in einer Stunde. Beliebige Sekundenzahl möglich. Für einen echten Discord-Zeitstempel: <t:{unixtimestamp+3600}:R>"),
        ("{role:ROLEID}", "Alle Mitglieder mit dieser Rolle, alphabetisch, als Erwähnungen."),
        ("{role:ROLEID:DESC}", "Wie oben, aber umgekehrt sortiert (Z→A). Ohne Zusatz bzw. :ASC ist die Reihenfolge A→Z.")
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

            if (text.Contains("{role:"))
                foreach (Match match in Regex.Matches(text, @"\{role:(\d+)(?::(ASC|DESC))?\}",
                             RegexOptions.IgnoreCase))
                {
                    var roleId = ulong.Parse(match.Groups[1].Value);
                    var descending = string.Equals(match.Groups[2].Value, "DESC",
                        StringComparison.OrdinalIgnoreCase);
                    text = text.Replace(match.Value, ResolveRoleMembers(guild, roleId, descending));
                }
        }

        return SnippetManagerHelper.FormatStringWithVariables(text);
    }

    private static string ResolveRoleMembers(DiscordGuild guild, ulong roleId, bool descending)
    {
        var role = guild.GetRole(roleId);
        if (role is null) return "*(Rolle nicht gefunden)*";

        var members = guild.Members.Values
            .Where(m => m.Roles.Any(r => r.Id == roleId))
            .OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (descending) members.Reverse();

        if (members.Count == 0) return "*(Niemand mit dieser Rolle)*";

        var sb = new StringBuilder();
        var shown = 0;
        foreach (var member in members)
        {
            var line = $"{member.Mention}\n";
            if (sb.Length + line.Length > RoleListBudget) break;
            sb.Append(line);
            shown++;
        }

        if (shown < members.Count) sb.Append($"*... und {members.Count - shown} weitere*");
        return sb.ToString().TrimEnd();
    }
}
