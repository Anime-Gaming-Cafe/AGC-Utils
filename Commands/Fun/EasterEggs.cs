﻿#region

using AGC_Management.Attributes;

#endregion

namespace AGC_Management.Commands.Fun;

public class AGCEasterEggs : BaseCommandModule
{
    [AGCEasterEggsEnabled]
    [Command("savas")]
    public async Task Savas(CommandContext ctx)
    {
        await ctx.Channel.SendMessageAsync("This Command is currently disabled :3");
    }
}
