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
    [SlashCommand("reward-roles", "Setzt oder Entfernt Belohnungsrollen", (long)Permissions.ManageGuild)]
    public static async Task RewardRoleCommand(InteractionContext ctx,
        [Option("action", "Die auszuführende Aktion")]
        ModifyRoleChannelAction aktion,
        [Option("role", "Die Rolle die hinzugefügt oder entfernt werden soll")]
        DiscordRole role,
        [Option("level", "Das Level ab dem die Rolle vergeben werden soll")]
        int level)
    {
        if (role.IsManaged)
        {
            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent(
                        "<:attention:1085333468688433232> **Fehler!** Die Rolle ist eine Integration Rolle und kann nicht bearbeitet werden!")
                    .AsEphemeral());
            return;
        }

        if (aktion == ModifyRoleChannelAction.Add && await LevelUtils.IsRewardRole(role.Id))
        {
            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent(
                        "<:attention:1085333468688433232> **Fehler!** Die Rolle wird bereits als Belohnungsrolle verwendet!")
                    .AsEphemeral());
            return;
        }

        if (aktion == ModifyRoleChannelAction.Remove && !await LevelUtils.IsRewardRole(role.Id))
        {
            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent(
                        "<:attention:1085333468688433232> **Fehler!** Die Rolle wird nicht als Belohnungsrolle verwendet!")
                    .AsEphemeral());
            return;
        }

        if (aktion == ModifyRoleChannelAction.Add && await LevelUtils.IsRewardLevel(level))
        {
            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent(
                        "<:attention:1085333468688433232> **Fehler!** Das Level wird bereits als Belohnungslevel verwendet!")
                    .AsEphemeral());
            return;
        }

        if (aktion == ModifyRoleChannelAction.Remove && !await LevelUtils.IsRewardLevel(level))
        {
            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent(
                        "<:attention:1085333468688433232> **Fehler!** Das Level wird nicht als Belohnungslevel verwendet!")
                    .AsEphemeral());
            return;
        }

        await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder()
                .WithContent("<a:loading_agc:1084157150747697203> Aktion wird ausgeführt...").AsEphemeral());
        if (aktion == ModifyRoleChannelAction.Add)
        {
            await LevelUtils.AddRewardRole(role.Id, level);
            await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                $"<:success:1085333481820790944> **Erfolgreich!** {role.Mention} wird nun ab Level {level} als Belohnungsrolle vergeben!"));
        }
        else if (aktion == ModifyRoleChannelAction.Remove)
        {
            await LevelUtils.RemoveRewardRole(role.Id, level);
            await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                $"<:success:1085333481820790944> **Erfolgreich!** {role.Mention} wird nun nicht mehr ab Level {level} als Belohnungsrolle vergeben!"));
        }
    }
}