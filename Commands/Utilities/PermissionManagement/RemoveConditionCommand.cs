#region

using AGC_Management.Providers;
using AGC_Management.Services;
using DisCatSharp.ApplicationCommands.Attributes;
using DisCatSharp.ApplicationCommands.Context;

#endregion

namespace AGC_Management.Commands.Perms;

public partial class Perms
{
    [ApplicationCommandRequirePermissions(Permissions.Administrator)]
    [SlashCommand("remove-condition", "Entfernt eine Bedingung von einer Permission")]
    public static async Task RemoveCondition(InteractionContext ctx,
        [Autocomplete(typeof(ExtraPermissionAutocompleteProvider))]
        [Option("permission", "Die Permission", true)]
        string permName,
        [Autocomplete(typeof(ExtraPermissionConditionAutocompleteProvider))]
        [Option("condition", "Die zu entfernende Bedingung", true)]
        string conditionId)
    {
        var permission = await ExtraPermissionService.GetPermissionAsync(permName);
        if (permission == null)
        {
            await RespondErrorAsync(ctx, $"Es gibt keine Permission mit dem Namen ``{permName}``.");
            return;
        }

        if (permission.Conditions.All(c => c.ConditionId != conditionId))
        {
            await RespondErrorAsync(ctx, $"``{conditionId}`` gehört nicht zu ``{permName}``.");
            return;
        }

        await ExtraPermissionService.RemoveConditionAsync(conditionId);

        var updated = await ExtraPermissionService.GetPermissionAsync(permName);
        var note = updated is { Conditions.Count: 0 }
            ? "\n\n<:attention:1085333468688433232> Die Permission hat jetzt keine Bedingungen mehr und wirkt nur noch manuell."
            : "";

        await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder()
                .WithContent($"<:success:1085333481820790944> **Erfolgreich!** Bedingung ``{conditionId}`` entfernt." +
                             $"\n\n{BuildPermissionLine(updated ?? permission)}{note}"));
    }
}
