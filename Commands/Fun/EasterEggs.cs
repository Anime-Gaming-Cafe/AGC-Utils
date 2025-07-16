#region

using AGC_Management.Attributes;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

#endregion

namespace AGC_Management.Commands.Fun;

public class AGCEasterEggs : BaseCommandModule
{
    private static readonly ConcurrentDictionary<ulong, DateTime> _cooldowns = new();
    private static readonly Random _rng = new();

    [AGCEasterEggsEnabled]
    [Command("savas")]
    public async Task Savas(CommandContext ctx)
    {
        var guildId = ctx.Guild?.Id ?? ctx.Channel.Id;

        if (_cooldowns.TryGetValue(guildId, out var lastUsed))
        {
            var now = DateTime.UtcNow;
            if (now < lastUsed)
            {
                var remaining = (int)(lastUsed - now).TotalSeconds;
                await ctx.RespondAsync($"⏳ Savaş hatte eben erst eine Tomate, erinnere ihn gerne später erneut :3");
                return;
            }
        }

        int cooldown = _rng.Next(60, 501);
        _cooldowns[guildId] = DateTime.UtcNow.AddSeconds(cooldown);

        await ctx.Channel.SendMessageAsync("POV <@443114493992763392>:\n### Ich liebe Tomaten <3\n[￶](https://meow.justabrian.me/-fzQMJB2y4P/Ryuunosuke_Akasaka.webp)");
    }
}
