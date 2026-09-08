#region

using AGC_Management.Services;
using DisCatSharp.ApplicationCommands.Attributes;
using DisCatSharp.ApplicationCommands.Context;

#endregion

namespace AGC_Management.Commands.Perms;

public partial class Perms
{
    [ApplicationCommandRequirePermissions(Permissions.Administrator)]
    [SlashCommand("settings", "Globale Einstellungen der Extra Permissions")]
    public static async Task Settings(InteractionContext ctx,
        [Option("auto-revoke", "Rolle entziehen wenn die Trigger-Bedingung wegfällt")]
        bool? autoRevoke = null)
    {
        if (autoRevoke.HasValue) await ExtraPermissionService.SetGlobalAutoRevokeAsync(autoRevoke.Value);

        var current = await ExtraPermissionService.GetGlobalAutoRevokeAsync();
        var prefix = autoRevoke.HasValue
            ? "<:success:1085333481820790944> **Erfolgreich!** Einstellung gespeichert.\n\n"
            : "";

        await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder()
                .WithContent($"{prefix}**Auto-Revoke bei Verlust der Bedingung:** {(current ? "an" : "aus")}\n" +
                             "Gilt für alle Permissions mit ``auto-revoke: Inherit``. Einzelne Permissions " +
                             "können das über ``/perms edit-permission-role`` überschreiben."));
    }
}
