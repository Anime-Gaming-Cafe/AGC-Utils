#region

using AGC_Management.Enums.LevelSystem;
using AGC_Management.Utils;
using DisCatSharp.ApplicationCommands.Attributes;
using DisCatSharp.ApplicationCommands.Context;

#endregion

namespace AGC_Management.Commands.Levelsystem;

public partial class LevelSystemSettings
{
    [ApplicationCommandRequirePermissions(Permissions.ManageGuild)]
    [SlashCommand("override-roles", "Setzt oder Entfernt Overrideroles",
        (long)Permissions.ManageGuild)]
    public static async Task ManageOverrideRoles(InteractionContext ctx,
        [Option("action", "Die auszuführende Aktion")]
        ModifyRoleChannelAction aktion,
        [Option("role", "Die Rolle die hinzugefügt oder entfernt werden soll")]
        DiscordRole role, [Option("multiplier", "Der Multiplier")] OverrideMultiplicatorItem multiplier)
    {
        var _multiplier = float.Parse(multiplier.ToString().Replace("x", "").Replace("X", "").Replace(" ", ""));
        if (role.IsManaged)
        {
            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent(
                        "<:attention:1085333468688433232> **Fehler!** Die Rolle ist eine Integration Rolle und kann nicht bearbeitet werden!")
                    .AsEphemeral());
            return;
        }

        if (aktion == ModifyRoleChannelAction.Add && await LevelUtils.IsOverrideRole(role.Id))
        {
            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent(
                        "<:attention:1085333468688433232> **Fehler!** Die Rolle wird bereits als Overriderole verwendet!")
                    .AsEphemeral());
            return;
        }

        if (aktion == ModifyRoleChannelAction.Remove && !await LevelUtils.IsOverrideRole(role.Id))
        {
            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent(
                        "<:attention:1085333468688433232> **Fehler!** Die Rolle wird nicht als Overriderole verwendet!")
                    .AsEphemeral());
            return;
        }

        await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder()
                .WithContent("<a:loading_agc:1084157150747697203> Aktion wird ausgeführt...")
                .AsEphemeral());
        if (aktion == ModifyRoleChannelAction.Add)
        {
            await LevelUtils.AddOverrideRole(role.Id, _multiplier);
            await ctx.EditResponseAsync(new DiscordWebhookBuilder()
                .WithContent(
                    $"<:success:1085333481820790944> **Erfolgreich!** {role.Mention} wird nun als Overriderole verwendet!"));
        }
        else if (aktion == ModifyRoleChannelAction.Remove)
        {
            await LevelUtils.RemoveOverrideRole(role.Id);
            await ctx.EditResponseAsync(new DiscordWebhookBuilder()
                .WithContent(
                    $"<:success:1085333481820790944> **Erfolgreich!** {role.Mention} wird nun nicht mehr als Overriderole verwendet!"));
        }
    }
}