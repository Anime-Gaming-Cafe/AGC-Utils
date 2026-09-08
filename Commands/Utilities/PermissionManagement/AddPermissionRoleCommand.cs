#region

using AGC_Management.Entities.ExtraPermissions;
using AGC_Management.Enums.ExtraPermissions;
using AGC_Management.Services;
using AGC_Management.Utils;
using DisCatSharp.ApplicationCommands.Attributes;
using DisCatSharp.ApplicationCommands.Context;

#endregion

namespace AGC_Management.Commands.Perms;

public partial class Perms
{
    [ApplicationCommandRequirePermissions(Permissions.Administrator)]
    [SlashCommand("add-permission-role", "Legt eine neue Extra Permission an")]
    public static async Task AddPermissionRole(InteractionContext ctx,
        [Option("name", "Name der Permission, frei wählbar")]
        string name,
        [Option("role", "Die Rolle die vergeben werden soll")]
        DiscordRole role,
        [Option("trigger", "Erste Bedingung, weitere über add-condition")]
        ExtraPermissionConditionType trigger = ExtraPermissionConditionType.None,
        [Option("value", "Level, Tage, Nachrichten oder Voice-Minuten - je nach Bedingung")]
        int value = 0,
        [Option("trigger-role", "Die Rolle auf die die Rollen-Bedingung hört")]
        DiscordRole? triggerRole = null,
        [Option("scope-channel", "Kanal oder Kategorie auf den die Bedingung eingeschränkt wird")]
        DiscordChannel? scopeChannel = null,
        [Option("scope-ids", "Weitere Kanal-/Kategorie-IDs, mit Komma getrennt")]
        string? scopeIdsRaw = null,
        [Option("window-days", "Nur die letzten X Tage zählen, 0 = alles")]
        int windowDays = 0,
        [Option("comparator", "Schwelle als Mindest- oder Höchstwert")]
        ExtraPermissionComparator comparator = ExtraPermissionComparator.Gte,
        [Option("negate", "Bedingung umkehren")]
        bool negate = false,
        [Option("mode", "Wiederkehrend oder einmalig")]
        ExtraPermissionTriggerMode mode = ExtraPermissionTriggerMode.Recurring,
        [Option("auto-revoke", "Rolle entziehen wenn die Bedingungen wegfallen")]
        ExtraPermissionAutoRevoke autoRevoke = ExtraPermissionAutoRevoke.Inherit,
        [Option("description", "Wofür ist diese Permission gedacht?")]
        string description = "")
    {
        var permName = ExtraPermissionService.Slugify(name);
        if (string.IsNullOrWhiteSpace(permName))
        {
            await RespondErrorAsync(ctx, "Der Name enthält keine verwertbaren Zeichen.");
            return;
        }

        if (await ExtraPermissionService.GetPermissionAsync(permName) != null)
        {
            await RespondErrorAsync(ctx, $"Eine Permission mit dem Namen ``{permName}`` existiert bereits.");
            return;
        }

        var roleError = await ValidateRoleAsync(ctx, role);
        if (roleError != null)
        {
            await RespondErrorAsync(ctx, roleError);
            return;
        }

        ExtraPermissionCondition? condition = null;
        if (trigger != ExtraPermissionConditionType.None)
        {
            if (!TryParseScopeIds(scopeIdsRaw, scopeChannel, out var scopeIds, out var scopeError))
            {
                await RespondErrorAsync(ctx, scopeError!);
                return;
            }

            var conditionError = BuildCondition(trigger, value, triggerRole, scopeIds, windowDays, comparator,
                negate, 1, role.Id, permName, out condition);
            if (conditionError != null)
            {
                await RespondErrorAsync(ctx, conditionError);
                return;
            }
        }

        var permission = new ExtraPermission
        {
            PermName = permName,
            DisplayName = name.Trim(),
            Description = description,
            RoleId = role.Id,
            TriggerMode = mode,
            AutoRevoke = autoRevoke,
            CreatedBy = ctx.User.Id,
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        await ExtraPermissionService.AddPermissionAsync(permission);
        if (condition != null)
        {
            await ExtraPermissionService.AddConditionAsync(condition);
            permission.Conditions.Add(condition);
        }

        await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder()
                .WithContent($"<:success:1085333481820790944> **Erfolgreich!** Permission angelegt.\n\n" +
                             BuildPermissionLine(permission)));
    }
}
