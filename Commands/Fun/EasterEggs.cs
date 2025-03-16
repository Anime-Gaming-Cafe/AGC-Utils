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
        await ctx.Channel.SendMessageAsync("POV <@443114493992763392>:");
        await ctx.Channel.SendMessageAsync("# Ich liebe Tomaten");
        await ctx.Channel.SendMessageAsync(
            "https://i.imgur.com/BTAcv4B.gif");
    }
}
