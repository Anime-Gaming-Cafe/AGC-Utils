#region

using AGC_Management.Providers;
using AGC_Management.Services;
using DisCatSharp.ApplicationCommands.Attributes;
using DisCatSharp.ApplicationCommands.Context;

#endregion

namespace AGC_Management.Commands.PermissionManagement;

public partial class PermissionManagement
{
    [SlashCommand("remove-permission-role", "Löscht eine Extra Permission", (long)Permissions.Administrator)]
    public static async Task RemovePermissionRole(InteractionContext ctx,
        [Autocomplete(typeof(ExtraPermissionAutocompleteProvider))]
        [Option("permission", "Die zu löschende Permission", true)]
        string permName,
        [Option("strip-role", "Die Rolle allen Mitgliedern entziehen die sie besitzen")]
        bool stripRole = false)
    {
        var permission = await ExtraPermissionService.GetPermissionAsync(permName);
        if (permission == null)
        {
            await RespondErrorAsync(ctx, $"Es gibt keine Permission mit dem Namen ``{permName}``.");
            return;
        }

        await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder()
                .WithContent("<a:loading_agc:1084157150747697203> Aktion wird ausgeführt...").AsEphemeral());

        var stripped = 0;
        if (stripRole)
        {
            var role = ctx.Guild.GetRole(permission.RoleId);
            if (role != null)
                foreach (var member in ctx.Guild.Members.Values.Where(m => m.Roles.Any(r => r.Id == role.Id)))
                    try
                    {
                        await member.RevokeRoleAsync(role, $"ExtraPermission {permName} gelöscht");
                        stripped++;
                    }
                    catch (Exception e)
                    {
                        CurrentApplication.Logger.Error(e,
                            "ExtraPermissions: failed to strip role for member {MemberId}", member.Id);
                    }
        }

        await ExtraPermissionService.DeletePermissionAsync(permName);

        var stripText = stripRole ? $" Die Rolle wurde {stripped} Mitgliedern entzogen." : "";
        await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
            $"<:success:1085333481820790944> **Erfolgreich!** Permission ``{permName}`` wurde gelöscht.{stripText}"));
    }
}
