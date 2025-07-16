#region

using AGC_Management.Attributes;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

#endregion

namespace AGC_Management.Commands.Fun;

public class AGCEasterEggs : BaseCommandModule
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    private class RandomGuildCooldownAttribute : CheckBaseAttribute
    {
        private static readonly ConcurrentDictionary<ulong, DateTime> _cooldowns = new();
        private readonly int _minSeconds;
        private readonly int _maxSeconds;

        public RandomGuildCooldownAttribute(int minSeconds, int maxSeconds)
        {
            _minSeconds = minSeconds;
            _maxSeconds = maxSeconds;
        }

        public override Task<bool> ExecuteCheckAsync(CommandContext ctx, bool help)
        {
            var guildId = ctx.Guild?.Id ?? ctx.Channel.Id;

            if (_cooldowns.TryGetValue(guildId, out var lastUsed))
            {
                var now = DateTime.UtcNow;

                if (now < lastUsed)
                {
                    var remaining = (int)(lastUsed - now).TotalSeconds;
                    _ = ctx.RespondAsync($"⏳ Savaş hatte eben erst eine Tomate, erinnere ihn gerne später erneut :3");
                    return Task.FromResult(false);
                }
            }

            var cooldownDuration = new Random().Next(_minSeconds, _maxSeconds + 1);
            _cooldowns[guildId] = DateTime.UtcNow.AddSeconds(cooldownDuration);

            return Task.FromResult(true);
        }
    }

    [AGCEasterEggsEnabled]
    [RandomGuildCooldown(60, 500)]
    [Command("savas")]
    public async Task Savas(CommandContext ctx)
    {
        await ctx.Channel.SendMessageAsync("POV <@443114493992763392>:\n### Ich liebe Tomaten <3\n[￶](https://meow.justabrian.me/-fzQMJB2y4P/Ryuunosuke_Akasaka.webp)");
    }
}