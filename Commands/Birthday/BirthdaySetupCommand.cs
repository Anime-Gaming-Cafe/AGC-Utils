#region

using AGC_Management.Services;
using DisCatSharp.ApplicationCommands;
using DisCatSharp.ApplicationCommands.Attributes;
using DisCatSharp.ApplicationCommands.Context;

#endregion

namespace AGC_Management.Commands.Birthday;

public class BirthdaySetupCommand : ApplicationCommandsModule
{
    [SlashCommand("birthdaysetup", "Richte deinen Geburtstag ein und verwalte den Geburtstagsping.")]
    public static async Task BirthdaySetup(InteractionContext ctx)
    {
        if (await BirthdayService.RejectIfDisabledAsync(ctx.Interaction)) return;

        var (embed, buttons) = await BirthdayService.BuildPanelAsync(ctx.User);
        await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().AddEmbed(embed).AddComponents(buttons).AsEphemeral());
    }
}
