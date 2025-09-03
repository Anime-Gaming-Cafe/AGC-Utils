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
        [Option("duration", "Dauer der Multiplikation")] long duration,
        [Option("time_unit", "Zeiteinheit")] TimeUnit timeUnit,
        [Option("reset_multiplier", "Multiplier-Wert nach Ablauf")] MultiplicatorItem resetMultiplier)
    {
        // Validate duration based on time unit
        long maxDuration = timeUnit switch
        {
            TimeUnit.Minutes => 10080, // 7 days in minutes
            TimeUnit.Hours => 168,     // 7 days in hours
            TimeUnit.Days => 7,        // 7 days
            TimeUnit.Weeks => 1,       // 1 week max
            TimeUnit.Months => 1,      // 1 month max
            _ => 168
        };

        if (duration < 1 || duration > maxDuration)
        {
            var timeUnitName = timeUnit switch
            {
                TimeUnit.Minutes => "Minuten",
                TimeUnit.Hours => "Stunden", 
                TimeUnit.Days => "Tage",
                TimeUnit.Weeks => "Wochen",
                TimeUnit.Months => "Monate",
                _ => "Stunden"
            };

            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent($"<:error:1085333484253687808> **Fehler!** Die Dauer muss zwischen 1 und {maxDuration} {timeUnitName} liegen!")
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
        
        if (multiplierValue <= 0 || (resetMultiplier != MultiplicatorItem.Disabled && resetValue < 0))
        {
            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent("<:error:1085333484253687808> **Fehler!** Ungültige Multiplier-Werte!")
                    .AsEphemeral());
            return;
        }
        
        long durationInSeconds = LevelUtils.ConvertTimeUnitToSeconds(duration, timeUnit);

        try
        {
            // Set the timed multiplier
            await LevelUtils.SetTimedMultiplier(levelType, multiplierValue, durationInSeconds, resetValue);

            var resetText = resetMultiplier != MultiplicatorItem.Disabled 
                ? $"{resetValue}x" 
                : "deaktiviert";

            var timeUnitDisplay = timeUnit switch
            {
                TimeUnit.Minutes => "Minuten",
                TimeUnit.Hours => "Stunden",
                TimeUnit.Days => "Tage", 
                TimeUnit.Weeks => "Wochen",
                TimeUnit.Months => "Monate",
                _ => "Stunden"
            };

            var typeText = levelType == XpRewardType.All ? "Message und Voice" : levelType.ToString();

            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent(
                        $"<:success:1085333481820790944> **Erfolgreich!** " +
                        $"Zeitlicher Multiplier für ``{typeText}`` wurde auf ``{multiplierValue}x`` " +
                        $"für ``{duration} {timeUnitDisplay}`` gesetzt! " +
                        $"Nach Ablauf wird er auf ``{resetText}`` zurückgesetzt."));
        }
        catch (Exception ex)
        {
            CurrentApplication.Logger.Error(ex, "Error setting timed multiplier");
            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent($"<:error:1085333484253687808> **Fehler!** Ein unerwarteter Fehler ist aufgetreten.")
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

            var typeText = levelType == XpRewardType.All ? "Message und Voice" : levelType.ToString();

            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent(
                        $"<:success:1085333481820790944> **Erfolgreich!** " +
                        $"Zeitlicher Multiplier für ``{typeText}`` wurde entfernt!"));
        }
        catch (Exception ex)
        {
            CurrentApplication.Logger.Error(ex, "Error removing timed multiplier");
            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent($"<:error:1085333484253687808> **Fehler!** Ein unerwarteter Fehler ist aufgetreten.")
                    .AsEphemeral());
        }
    }
}