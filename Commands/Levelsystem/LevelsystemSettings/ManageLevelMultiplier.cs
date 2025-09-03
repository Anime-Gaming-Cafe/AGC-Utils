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
        [Option("multiplier", "Der Multiplier")]
        MultiplicatorItem multiplier)
    {
        float _multiplier;

        if (multiplier != MultiplicatorItem.Disabled)
            _multiplier = LevelUtils.GetFloatFromMultiplicatorItem(multiplier);
        else
            _multiplier = 0;

        // set multiplier (if disabled, type_active = false)
        await LevelUtils.SetMultiplier(levelType, _multiplier);


        if (multiplier != MultiplicatorItem.Disabled)
        {
            var typeText = levelType == XpRewardType.All ? "Message und Voice" : levelType.ToString();
            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent(
                        $"<:success:1085333481820790944> **Erfolgreich!** Der Multiplier für ``{typeText}`` wurde auf ``{LevelUtils.GetFloatFromMultiplicatorItem(multiplier)}`` gesetzt!"));
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