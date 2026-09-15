#region

using System.Diagnostics;
using System.Reflection;
using AGC_Management.Utils;

#endregion

namespace AGC_Management.Commands;

public class StatsCommand : BaseCommandModule
{
    [Command("stats")]
    [Description("Shows the bot's stats.")]
    [Aliases("botinfo", "botstats", "bot")]
    public async Task Stats(CommandContext ctx)
    {
        var embed = new DiscordEmbedBuilder();
        embed.WithTitle("AGC Management Bot Stats")
            .WithColor(DiscordColor.Blurple)
            .WithThumbnail(ctx.Client.CurrentUser.AvatarUrl)
            .WithFooter($"Requested by {ctx.User.Username}",
                ctx.User.AvatarUrl)
            .WithTimestamp(DateTimeOffset.Now);

        var uptime = DateTime.Now - Process.GetCurrentProcess().StartTime;
        var uptimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - uptime.TotalSeconds;
        var rounduptime = Math.Round(uptimestamp, 0);
        var istring = $"Uptime: <t:{rounduptime}:f> <t:{rounduptime}:R>\n";

        var compiledate = ToolSet.GetBuildDateToUnixTime(Assembly.GetExecutingAssembly());
        istring += $"Compile Date: <t:{compiledate}:f> <t:{compiledate}:R>\n";

        var buildnumber = ToolSet.GetBuildNumber(Assembly.GetExecutingAssembly());
        istring += $"Build Number: **{buildnumber}**\n";

        var BotVersion = CurrentApplication.VersionString;
        istring += $"Bot Version: **{BotVersion}**\n";

        var cachedUsers = ctx.Client.UserCache.Count;
        istring += $"Cached Users: **{cachedUsers}**\n";

        var ramUsage = Process.GetCurrentProcess().WorkingSet64;
        istring += $"RAM Usage: **{ramUsage / 1024 / 1024} MB**\n";

        var latency = ctx.Client.Ping;
        istring += $"Ping: **{latency} ms**\n";

        var netruntime = Environment.Version;
        istring += $"Microsoft .NET Runtime: **{netruntime}**\n";

        var libraryversion = ctx.Client.BotLibrary + " " + ctx.Client.VersionString;
        istring += $"Library Version: **{libraryversion}**\n";

        var OS = Environment.OSVersion;
        istring += $"Operating System: **{OS}**\n";

        var workingdir = Environment.ProcessPath;
        istring += $"Working Directory: **{workingdir}**\n";

        var cpu = Environment.ProcessorCount;
        istring += $"CPU Cores: **{cpu}**\n";

        var botowner = ctx.Client.CurrentApplication.Owner;
        istring += $"Bot Owner: **{botowner.GetFormattedUserName()}** ``{botowner.Id}``\n";

        embed.WithDescription(istring);

        await ctx.RespondAsync(embed);
    }
}