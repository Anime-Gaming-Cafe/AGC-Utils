#region

using System.Text;
using AGC_Management.Entities.ExtraPermissions;
using AGC_Management.Enums.ExtraPermissions;
using AGC_Management.Utils;
using DisCatSharp.ApplicationCommands;
using DisCatSharp.ApplicationCommands.Attributes;
using DisCatSharp.ApplicationCommands.Context;
using DisCatSharp.Exceptions;

#endregion

namespace AGC_Management.Commands.PermissionManagement;

[ApplicationCommandRequirePermissions(Permissions.Administrator)]
[SlashCommandGroup("permissionmanagement", "Verwaltung der Extra Permissions", (long)Permissions.Administrator)]
public partial class PermissionManagement : ApplicationCommandsModule
{
    private static readonly ExtraPermissionConditionType[] ValueConditions =
    [
        ExtraPermissionConditionType.Level,
        ExtraPermissionConditionType.MembershipAge,
        ExtraPermissionConditionType.Messages,
        ExtraPermissionConditionType.VoiceMinutes
    ];

    private static Task RespondErrorAsync(InteractionContext ctx, string message)
    {
        return ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder()
                .WithContent($"<:attention:1085333468688433232> **Fehler!** {message}")
                .AsEphemeral());
    }

    private static async Task<string?> ValidateRoleAsync(InteractionContext ctx, DiscordRole role)
    {
        if (role.IsManaged)
            return "Die Rolle wird von einer Integration verwaltet und kann nicht vergeben werden.";

        if (role.Id == ctx.Guild.EveryoneRole.Id)
            return "Die @everyone Rolle kann nicht als Permission-Rolle verwendet werden.";

        try
        {
            var bot = await ctx.Guild.GetMemberAsync(ctx.Client.CurrentUser.Id);
            var highest = bot.Roles.MaxBy(r => r.Position);
            if (highest == null || role.Position >= highest.Position)
                return "Die Rolle liegt auf oder über der höchsten Bot-Rolle und kann deshalb nicht vergeben werden.";
        }
        catch (NotFoundException)
        {
            return "Die Bot-Rolle konnte nicht ermittelt werden.";
        }

        return null;
    }

    private static bool TryParseScopeIds(string? raw, DiscordChannel? scopeChannel, out long[] scopeIds,
        out string? error)
    {
        var ids = new List<long>();
        error = null;

        if (scopeChannel != null) ids.Add((long)scopeChannel.Id);

        if (!string.IsNullOrWhiteSpace(raw))
            foreach (var part in raw.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (!ulong.TryParse(part.Trim(), out var parsed))
                {
                    scopeIds = [];
                    error = $"``{part.Trim()}`` ist keine gültige Kanal- oder Kategorie-ID.";
                    return false;
                }

                ids.Add((long)parsed);
            }

        scopeIds = ids.Distinct().ToArray();
        return true;
    }

    private static string? BuildCondition(ExtraPermissionConditionType type, int value, DiscordRole? triggerRole,
        long[] scopeIds, int windowDays, ExtraPermissionComparator comparator, bool negate, int group,
        ulong grantedRoleId, string permName, out ExtraPermissionCondition? condition)
    {
        condition = null;

        if (type == ExtraPermissionConditionType.None)
            return "Bitte eine Bedingungsart angeben.";

        long conditionValue = 0;

        if (type == ExtraPermissionConditionType.Role)
        {
            if (triggerRole == null) return "Die Bedingung **Role** benötigt die Option ``trigger-role``.";
            if (triggerRole.Id == grantedRoleId)
                return "Bedingungsrolle und vergebene Rolle dürfen nicht identisch sein.";

            conditionValue = (long)triggerRole.Id;
        }
        else if (ValueConditions.Contains(type))
        {
            if (value < 0) return "``value`` darf nicht negativ sein.";
            if (value == 0 && comparator == ExtraPermissionComparator.Gte)
                return $"Die Bedingung **{type}** benötigt ``value`` mit einem Wert größer als 0.";

            conditionValue = value;
        }

        if (windowDays < 0) return "``window-days`` darf nicht negativ sein.";
        if (group < 1) return "``group`` muss mindestens 1 sein.";

        var probe = new ExtraPermissionCondition { Type = type };
        if (scopeIds.Length > 0 && !probe.SupportsScope)
            return $"Die Bedingung **{type}** unterstützt keinen Kanal-Scope.";
        if (windowDays > 0 && !probe.SupportsWindow)
            return $"Die Bedingung **{type}** unterstützt kein Zeitfenster.";

        condition = new ExtraPermissionCondition
        {
            ConditionId = ToolSet.GenerateCaseID(),
            PermName = permName,
            GroupId = group,
            Type = type,
            Comparator = comparator,
            Value = conditionValue,
            ScopeIds = probe.SupportsScope ? scopeIds : [],
            WindowDays = probe.SupportsWindow ? windowDays : 0,
            Negate = negate,
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        return null;
    }

    private const int EmbedDescriptionLimit = 4000;

    private static string JoinWithinLimit(IEnumerable<string> blocks, out int shown, out int total)
    {
        var list = blocks.ToList();
        total = list.Count;
        shown = 0;

        var builder = new StringBuilder();
        foreach (var block in list)
        {
            var addition = builder.Length == 0 ? block : "\n\n" + block;
            if (builder.Length + addition.Length > EmbedDescriptionLimit) break;

            builder.Append(addition);
            shown++;
        }

        if (shown == 0 && list.Count > 0)
        {
            builder.Append(list[0][..Math.Min(list[0].Length, EmbedDescriptionLimit)]);
            shown = 1;
        }

        return builder.ToString();
    }

    private static string BuildPermissionLine(ExtraPermission permission)
    {
        var role = CurrentApplication.TargetGuild?.GetRole(permission.RoleId);
        var roleText = role?.Mention ?? $"``{permission.RoleId}``";
        var mode = ExtraPermissionFormatter.DescribeMode(permission);
        var modeText = string.IsNullOrEmpty(mode) ? "" : $" · {mode}";

        var line = $"**{permission.DisplayName}** ``{permission.PermName}``\n" +
                   $"Rolle: {roleText}{modeText} · Auto-Revoke: {ExtraPermissionFormatter.DescribeAutoRevoke(permission)}\n" +
                   $"Bedingungen:\n{ExtraPermissionFormatter.DescribeConditions(permission, "> ")}";

        if (!string.IsNullOrWhiteSpace(permission.Description))
            line += $"\n*{permission.Description}*";

        return line;
    }
}
