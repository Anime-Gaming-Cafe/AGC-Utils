#region

using AGC_Management.Services;
using DisCatSharp.ApplicationCommands.Attributes;
using DisCatSharp.ApplicationCommands.Context;

#endregion

namespace AGC_Management.Commands.PermissionManagement;

public partial class PermissionManagement
{
    [SlashCommand("list", "Zeigt alle angelegten Extra Permissions", (long)Permissions.Administrator)]
    public static async Task ListPermissions(InteractionContext ctx)
    {
        var permissions = await ExtraPermissionService.GetPermissionsAsync();
        if (permissions.Count == 0)
        {
            await RespondErrorAsync(ctx, "Es sind noch keine Extra Permissions angelegt.");
            return;
        }

        var autoRevoke = await ExtraPermissionService.GetGlobalAutoRevokeAsync();
        var description = JoinWithinLimit(permissions.Select(BuildPermissionLine), out var shown, out var total);
        var footer = $"Globales Auto-Revoke: {(autoRevoke ? "an" : "aus")}";
        if (shown < total) footer += $" · {shown}/{total} angezeigt";

        var embed = new DiscordEmbedBuilder()
            .WithTitle($"Extra Permissions ({permissions.Count})")
            .WithDescription(description)
            .WithColor(BotConfig.GetEmbedColor())
            .WithFooter(footer);

        await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().AddEmbed(embed).AsEphemeral());
    }
}
