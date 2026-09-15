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
    [SlashCommand("blacklisted-channels", "Setzt oder Entfernt Kanalblacklists", (long)Permissions.ManageGuild)]
    public static async Task ChannelBlacklistingCommands(InteractionContext ctx,
        [Option("action", "Die auszuführende Aktion")]
        ModifyRoleChannelAction aktion,
        [Option("channel", "Der Channel der hinzugefügt oder entfernt werden soll")]
        DiscordChannel channel)
    {
        if (aktion == ModifyRoleChannelAction.Add && await LevelUtils.IsBlacklistedChannel(channel.Id))
        {
            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent(
                        "<:attention:1085333468688433232> **Fehler!** Der Channel wird bereits als Blacklist Channel verwendet!")
                    .AsEphemeral());
            return;
        }

        if (aktion == ModifyRoleChannelAction.Remove && !await LevelUtils.IsBlacklistedChannel(channel.Id))
        {
            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent(
                        "<:attention:1085333468688433232> **Fehler!** Der Channel wird nicht als Blacklist Channel verwendet!")
                    .AsEphemeral());
            return;
        }

        await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder()
                .WithContent("<a:loading_agc:1084157150747697203> Aktion wird ausgeführt...").AsEphemeral());
        if (aktion == ModifyRoleChannelAction.Add)
        {
            await LevelUtils.AddBlacklistedChannel(channel.Id);
            await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                $"<:success:1085333481820790944> **Erfolgreich!** {channel.Mention} wird nun als Blacklist Channel verwendet!"));
        }
        else if (aktion == ModifyRoleChannelAction.Remove)
        {
            await LevelUtils.RemoveBlacklistedChannel(channel.Id);
            await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                $"<:success:1085333481820790944> **Erfolgreich!** {channel.Mention} wird nun nicht mehr als Blacklist Channel verwendet!"));
        }
    }
}