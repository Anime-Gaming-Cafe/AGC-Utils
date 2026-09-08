#region

using AGC_Management.Enums.ExtraPermissions;
using AGC_Management.Providers;
using AGC_Management.Services;
using DisCatSharp.ApplicationCommands.Attributes;
using DisCatSharp.ApplicationCommands.Context;

#endregion

namespace AGC_Management.Commands.PermissionManagement;

public partial class PermissionManagement
{
    [SlashCommand("add-condition", "Fügt einer Permission eine Bedingung hinzu", (long)Permissions.Administrator)]
    public static async Task AddCondition(InteractionContext ctx,
        [Autocomplete(typeof(ExtraPermissionAutocompleteProvider))]
        [Option("permission", "Die Permission", true)]
        string permName,
        [Option("type", "Art der Bedingung")] ExtraPermissionConditionType type,
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
        [Option("group", "Gruppe: gleiche Gruppe = ODER, verschiedene Gruppen = UND")]
        int group = 1)
    {
        var permission = await ExtraPermissionService.GetPermissionAsync(permName);
        if (permission == null)
        {
            await RespondErrorAsync(ctx, $"Es gibt keine Permission mit dem Namen ``{permName}``.");
            return;
        }

        if (!TryParseScopeIds(scopeIdsRaw, scopeChannel, out var scopeIds, out var scopeError))
        {
            await RespondErrorAsync(ctx, scopeError!);
            return;
        }

        var conditionError = BuildCondition(type, value, triggerRole, scopeIds, windowDays, comparator, negate,
            group, permission.RoleId, permName, out var condition);
        if (conditionError != null)
        {
            await RespondErrorAsync(ctx, conditionError);
            return;
        }

        await ExtraPermissionService.AddConditionAsync(condition!);

        var updated = await ExtraPermissionService.GetPermissionAsync(permName);
        await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder()
                .WithContent($"<:success:1085333481820790944> **Erfolgreich!** Bedingung ``{condition!.ConditionId}`` " +
                             $"zu ``{permName}`` hinzugefügt.\n\n" + BuildPermissionLine(updated ?? permission))
                .AsEphemeral());
    }
}
