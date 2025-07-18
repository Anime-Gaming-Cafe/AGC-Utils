#region

using AGC_Management.Attributes;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

#endregion

namespace AGC_Management.Commands.Fun;

public class AGCEasterEggs : BaseCommandModule
{
    private static readonly ConcurrentDictionary<ulong, DateTime> _savascooldown = new();
    private static readonly ConcurrentDictionary<ulong, DateTime> _briancooldown = new();
    private static readonly Random _rng = new();

    [AGCEasterEggsEnabled]
    [Command("savas")]
    public async Task Savas(CommandContext ctx)
    {
        ulong guildId = ctx.Guild?.Id ?? ctx.Channel.Id;

        if (_savascooldown.TryGetValue(guildId, out var lastUsed))
        {
            DateTime now = DateTime.UtcNow;
            if (now < lastUsed)
            {
                await ctx.RespondAsync($"⏳ Savaş hatte eben erst eine Tomate, erinnere ihn gerne später erneut :3");
                return;
            }
        }

        int cooldown = _rng.Next(60, 501);
        _savascooldown[guildId] = DateTime.UtcNow.AddSeconds(cooldown);

        await ctx.Channel.SendMessageAsync("POV <@443114493992763392>:\n### Ich liebe Tomaten <3\n[￶](https://meow.justabrian.me/-fzQMJB2y4P/Ryuunosuke_Akasaka.webp)");
    }
    
    [AGCEasterEggsEnabled]
    [Command("brian")]
    [Aliases("briana", "brain")]
    public async Task Brian(CommandContext ctx)
    {
        ulong guildId = ctx.Guild?.Id ?? ctx.Channel.Id;

        if (_briancooldown.TryGetValue(guildId, out var lastUsed))
        {
            DateTime now = DateTime.UtcNow;
            if (now < lastUsed)
            {
                await ctx.RespondAsync($"Tut mir leid, aber unser Brain wird sonst sauer, das wollen wir nicht!:3");
                return;
            }
        }

        List<string> messages = new();
        messages.Add("### The brain of <@515404778021322773> is not functional... pls try again later.\n[￶](https://tenor.com/view/yuru-yuri-anime-drool-drooling-loading-gif-15638770612162896677)");
        messages.Add("### <@515404778021322773>a? Fulltime Ladyboy, Glow-Up auf Anschlag.💅 \nBrain? Brain hat gekündigt.\n[￶](https://tenor.com/view/mayu-nekoyashiki-cure-lillian-wonderful-precure-anime-pretty-cure-gif-5404015643805058067)");

        int cooldown = _rng.Next(120, 1501);
        _briancooldown[guildId] = DateTime.UtcNow.AddSeconds(cooldown);

        int index = _rng.Next(messages.Count);
        string rndmmsg = messages[index];

        await ctx.Channel.SendMessageAsync(rndmmsg);
    }


 [AGCEasterEggsEnabled]
    [Command("nib")]
    public async Task Nib(CommandContext ctx)
    {
        ulong guildId = ctx.Guild?.Id ?? ctx.Channel.Id;

        if (_nibcooldown.TryGetValue(guildId, out var lastUsed))
        {
            DateTime now = DateTime.UtcNow;
            if (now < lastUsed)
            {
                await ctx.RespondAsync($"Nib hatte eben erst ein weißes Monster!");
                return;
            }
        }

        int cooldown = _rng.Next(60, 501);
        _nibcooldown[guildId] = DateTime.UtcNow.AddSeconds(cooldown);

        await ctx.Channel.SendMessageAsync("POV <@322712147345801217>:\n### Lecker weißes Monster\n[￶](https://tenor.com/view/white-monster-wmster-monster-energy-monster-energy-drink-monkey-gif-15628061433413475006)");
    }
}
