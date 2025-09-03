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
    [SlashCommand("manage-timed-leveling", "Verwaltet zeitliche Leveling-Multiplier", (long)Permissions.ManageGuild)]
    public static async Task ManageTimedLevelMultiplier(InteractionContext ctx,
        [Option("leveltype", "Der Leveltyp")] XpRewardType levelType,
        [Option("multiplier", "Der Multiplier")] MultiplicatorItem multiplier,
        [Option("duration_hours", "Dauer in Stunden (1-168)")] long durationHours,
        [Option("reset_multiplier", "Multiplier-Wert nach Ablauf")] MultiplicatorItem resetMultiplier)
    {
        // Validate input
        if (durationHours < 1 || durationHours > 168) // Max 7 days
        {
            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent("<:error:1085333484253687808> **Fehler!** Die Dauer muss zwischen 1 und 168 Stunden (7 Tage) liegen!")
                    .AsEphemeral());
            return;
        }

        if (multiplier == MultiplicatorItem.Disabled)
        {
            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent("<:error:1085333484253687808> **Fehler!** Der zeitliche Multiplier kann nicht deaktiviert sein!")
                    .AsEphemeral());
            return;
        }

        float multiplierValue = LevelUtils.GetFloatFromMultiplicatorItem(multiplier);
        float resetValue = resetMultiplier != MultiplicatorItem.Disabled 
            ? LevelUtils.GetFloatFromMultiplicatorItem(resetMultiplier) 
            : 0;
        
        long durationInSeconds = durationHours * 3600;

        try
        {
            // Set the timed multiplier
            await LevelUtils.SetTimedMultiplier(levelType, multiplierValue, durationInSeconds, resetValue);

            var resetText = resetMultiplier != MultiplicatorItem.Disabled 
                ? $"{resetValue}x" 
                : "deaktiviert";

            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent(
                        $"<:success:1085333481820790944> **Erfolgreich!** " +
                        $"Zeitlicher Multiplier für ``{levelType}`` wurde auf ``{multiplierValue}x`` " +
                        $"für ``{durationHours} Stunden`` gesetzt! " +
                        $"Nach Ablauf wird er auf ``{resetText}`` zurückgesetzt."));
        }
        catch (Exception ex)
        {
            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent($"<:error:1085333484253687808> **Fehler!** {ex.Message}")
                    .AsEphemeral());
        }
    }

    [ApplicationCommandRequirePermissions(Permissions.ManageGuild)]
    [SlashCommand("remove-timed-leveling", "Entfernt zeitliche Leveling-Multiplier", (long)Permissions.ManageGuild)]
    public static async Task RemoveTimedLevelMultiplier(InteractionContext ctx,
        [Option("leveltype", "Der Leveltyp")] XpRewardType levelType)
    {
        try
        {
            await LevelUtils.RemoveTimedMultiplier(levelType);

            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent(
                        $"<:success:1085333481820790944> **Erfolgreich!** " +
                        $"Zeitlicher Multiplier für ``{levelType}`` wurde entfernt!"));
        }
        catch (Exception ex)
        {
            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent($"<:error:1085333484253687808> **Fehler!** {ex.Message}")
                    .AsEphemeral());
        }
    }
}