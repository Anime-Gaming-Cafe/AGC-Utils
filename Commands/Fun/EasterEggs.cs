#region

using AGC_Management.Attributes;

#endregion

namespace AGC_Management.Commands.Fun;

public class AGCEasterEggs : BaseCommandModule
{
    [AGCEasterEggsEnabled]
    [Command("savas")]
    public async Task Savas(CommandContext ctx)
    {
        await ctx.Channel.SendMessageAsync("@thebladestream https://tenor.com/view/tomato-knife-cutting-cut-tomatoes-gif-6316343756749469417");
    }
}
