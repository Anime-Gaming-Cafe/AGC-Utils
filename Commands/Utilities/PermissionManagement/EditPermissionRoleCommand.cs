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
    [SlashCommand("edit-permission-role", "Bearbeitet eine bestehende Extra Permission",
        (long)Permissions.Administrator)]
    public static async Task EditPermissionRole(InteractionContext ctx,
        [Autocomplete(typeof(ExtraPermissionAutocompleteProvider))]
        [Option("permission", "Die zu bearbeitende Permission", true)]
        string permName,
        [Option("role", "Neue Rolle die vergeben werden soll")]
        DiscordRole? role = null,
        [Option("mode", "Wiederkehrend oder einmalig")]
        ExtraPermissionTriggerMode? mode = null,
        [Option("auto-revoke", "Rolle entziehen wenn die Bedingungen wegfallen")]
        ExtraPermissionAutoRevoke? autoRevoke = null,
        [Option("name", "Neuer Anzeigename")] string? displayName = null,
        [Option("description", "Wofür ist diese Permission gedacht?")]
        string? description = null)
    {
        var permission = await ExtraPermissionService.GetPermissionAsync(permName);
        if (permission == null)
        {
            await RespondErrorAsync(ctx, $"Es gibt keine Permission mit dem Namen ``{permName}``.");
            return;
        }

        if (role != null)
        {
            var roleError = await ValidateRoleAsync(ctx, role);
            if (roleError != null)
            {
                await RespondErrorAsync(ctx, roleError);
                return;
            }

            if (permission.Conditions.Any(c =>
                    c.Type == ExtraPermissionConditionType.Role && (ulong)c.Value == role.Id))
            {
                await RespondErrorAsync(ctx,
                    "Die Rolle wird bereits als Bedingung dieser Permission verwendet und kann nicht gleichzeitig die vergebene Rolle sein.");
                return;
            }

            permission.RoleId = role.Id;
        }

        if (mode.HasValue) permission.TriggerMode = mode.Value;
        if (autoRevoke.HasValue) permission.AutoRevoke = autoRevoke.Value;
        if (displayName != null) permission.DisplayName = displayName.Trim();
        if (description != null) permission.Description = description;

        await ExtraPermissionService.UpdatePermissionAsync(permission);

        await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder()
                .WithContent("<:success:1085333481820790944> **Erfolgreich!** Permission aktualisiert.\n" +
                             "Bedingungen änderst du über ``add-condition`` und ``remove-condition``.\n\n" +
                             BuildPermissionLine(permission))
                .AsEphemeral());
    }
}
