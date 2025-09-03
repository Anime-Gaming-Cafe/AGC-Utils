#region

using AGC_Management.Entities;
using AGC_Management.Enums.LevelSystem;
using AGC_Management.Utils;
using DisCatSharp.ApplicationCommands.Attributes;
using DisCatSharp.ApplicationCommands.Context;

#endregion

namespace AGC_Management.Commands.Levelsystem;

public partial class LevelSystemSettings
{
    [ApplicationCommandRequirePermissions(Permissions.ManageGuild)]
    [SlashCommand("manage-leveling", "Verwaltet das Levelsystem", (long)Permissions.ManageGuild)]
    public static async Task MangeleLevelMulitplier(InteractionContext ctx,
        [Option("leveltype", "Der Leveltyp")] XpRewardType levelType,
        [Option("multiplier", "Der Multiplier (0 = deaktiviert, 0.01-100.0 = aktiv)")]
        double multiplier)
    {
        // Validate multiplier value
        if (multiplier < 0 || multiplier > 100.0)
        {
            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent("<:error:1085333484253687808> **Fehler!** Der Multiplier muss zwischen 0 und 100.0 liegen!")
                    .AsEphemeral());
            return;
        }

        float _multiplier = (float)multiplier;

        // set multiplier (if 0, type_active = false)
        await LevelUtils.SetMultiplier(levelType, _multiplier);

        if (multiplier > 0)
        {
            var typeText = levelType == XpRewardType.All ? "Message und Voice" : levelType.ToString();
            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent(
                        $"<:success:1085333481820790944> **Erfolgreich!** Der Multiplier für ``{typeText}`` wurde auf ``{_multiplier}x`` gesetzt!"));
        }
        else
        {
            var typeText = levelType == XpRewardType.All ? "Message und Voice" : levelType.ToString();
            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent(
                        $"<:success:1085333481820790944> **Erfolgreich!** Leveling für ``{typeText}`` wurde deaktiviert!"));
        }
    }
}