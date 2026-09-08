#region

using AGC_Management.Entities.ExtraPermissions;
using AGC_Management.Enums.ExtraPermissions;

#endregion

namespace AGC_Management.Utils;

public static class ExtraPermissionFormatter
{
    public static string DescribeCondition(ExtraPermissionCondition condition)
    {
        var guild = CurrentApplication.TargetGuild;
        var cmp = condition.Comparator == ExtraPermissionComparator.Lte ? "≤" : "≥";

        var text = condition.Type switch
        {
            ExtraPermissionConditionType.Level => $"Level {cmp} {condition.Value}",
            ExtraPermissionConditionType.Boost => "Boostet den Server",
            ExtraPermissionConditionType.Join => "Ist auf dem Server",
            ExtraPermissionConditionType.Voice => "In einem Sprachkanal",
            ExtraPermissionConditionType.Role =>
                $"Hat die Rolle {guild?.GetRole((ulong)condition.Value)?.Mention ?? $"``{condition.Value}``"}",
            ExtraPermissionConditionType.MembershipAge => $"Servermitglied {cmp} {condition.Value} Tage",
            ExtraPermissionConditionType.Messages => $"Nachrichten {cmp} {condition.Value}",
            ExtraPermissionConditionType.VoiceMinutes => $"Voice-Minuten {cmp} {condition.Value}",
            _ => "Unbekannt"
        };

        if (condition.SupportsScope && condition.ScopeIds.Length > 0)
            text += $" in {DescribeScope(condition.ScopeIds)}";

        if (condition.SupportsWindow && condition.WindowDays > 0)
            text += $" (letzte {condition.WindowDays} Tage)";

        return condition.Negate ? $"NICHT ({text})" : text;
    }

    private static string DescribeScope(long[] scopeIds)
    {
        var guild = CurrentApplication.TargetGuild;
        var parts = scopeIds.Select(raw =>
        {
            var id = (ulong)raw;
            if (guild != null && guild.Channels.TryGetValue(id, out var channel))
                return channel.Type == ChannelType.Category ? $"**{channel.Name}**" : channel.Mention;

            return $"``{id}``";
        });

        return string.Join(", ", parts);
    }

    public static string DescribeConditions(ExtraPermission permission, string indent = "")
    {
        if (permission.Conditions.Count == 0) return $"{indent}Keine Bedingungen (nur manuell)";

        var groups = permission.Conditions.GroupBy(c => c.GroupId).OrderBy(g => g.Key).ToList();
        var lines = new List<string>();

        foreach (var group in groups)
        {
            var members = group.ToList();
            for (var i = 0; i < members.Count; i++)
            {
                var prefix = i == 0 ? $"({group.Key})" : "   ODER";
                lines.Add($"{indent}{prefix} {DescribeCondition(members[i])}");
            }
        }

        return string.Join("\n", lines);
    }

    public static string DescribeMode(ExtraPermission permission)
    {
        if (permission.Conditions.Count == 0) return "";

        return permission.TriggerMode == ExtraPermissionTriggerMode.Once ? "einmalig" : "wiederkehrend";
    }

    public static string DescribeAutoRevoke(ExtraPermission permission)
    {
        return permission.AutoRevoke switch
        {
            ExtraPermissionAutoRevoke.On => "an",
            ExtraPermissionAutoRevoke.Off => "aus",
            _ => "global"
        };
    }

    public static string DescribeExpiry(ExtraPermissionMemberState state)
    {
        if (state.ExpiresAt <= 0) return "unbefristet";

        return
            $"läuft ab {Formatter.Timestamp(DateTimeOffset.FromUnixTimeSeconds(state.ExpiresAt), TimestampFormat.RelativeTime)}";
    }

    public static string DescribeStatus(ExtraPermissionStatus status)
    {
        var state = status.MemberState.EffectiveState;
        var icon = status.HasRole ? "✅" : "❌";

        var source = state switch
        {
            ExtraPermissionState.Granted =>
                $"manuell erteilt von {FormatActor(status.MemberState.ActorId)}, {DescribeExpiry(status.MemberState)}",
            ExtraPermissionState.Revoked =>
                $"manuell entzogen von {FormatActor(status.MemberState.ActorId)}, {DescribeExpiry(status.MemberState)}",
            _ => status.Permission.Conditions.Count == 0
                ? "keine Bedingungen, nicht manuell gesetzt"
                : status.ConditionMet
                    ? "automatisch, Bedingungen erfüllt"
                    : "automatisch, Bedingungen nicht erfüllt"
        };

        return $"{icon} {source}";
    }

    private static string FormatActor(ulong actorId)
    {
        return actorId == 0 ? "System" : $"<@{actorId}>";
    }

    public static string BuildUserInfoSection(List<ExtraPermissionStatus> statuses)
    {
        var relevant = statuses
            .Where(s => s.HasRole || s.MemberState.EffectiveState != ExtraPermissionState.Auto)
            .ToList();

        if (relevant.Count == 0) return "Es wurden keine gefunden.\n";

        var lines = relevant.Select(s =>
        {
            var manual = s.MemberState.EffectiveState switch
            {
                ExtraPermissionState.Granted => " *(manuell erteilt)*",
                ExtraPermissionState.Revoked => " *(manuell entzogen)*",
                _ => ""
            };

            var expiry = s.MemberState.ExpiresAt > 0 ? $" - {DescribeExpiry(s.MemberState)}" : "";
            var icon = s.HasRole ? "✅" : "❌";

            return $"{icon} **{s.Permission.DisplayName}** ``{s.Permission.PermName}``{manual}{expiry}";
        });

        return string.Join("\n", lines) + "\n";
    }
}
