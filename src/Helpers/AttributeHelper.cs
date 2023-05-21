using AGC_Management.Services.DatabaseHandler;
using DisCatSharp.CommandsNext;
using DisCatSharp.CommandsNext.Attributes;
using DisCatSharp.Entities;

namespace AGC_Management.Helpers;

public class RequireStaffRole : CheckBaseAttribute
{
    private readonly ulong RoleId = ulong.Parse(GlobalProperties.ConfigIni["ServerConfig"]["StaffRoleId"]);

    public override async Task<bool> ExecuteCheckAsync(CommandContext ctx, bool help)
    {
        // Check if user has staff role
        if (ctx.Member.Roles.Any(r => r.Id == RoleId))
            return true;
        return false;
    }
}

public class RequireDatabase : CheckBaseAttribute
{
    public override async Task<bool> ExecuteCheckAsync(CommandContext ctx, bool help)
    {
        // Check if database is connected
        if (DatabaseService.IsConnected()) return true;

        Console.WriteLine("Database is not connected! Command disabled.");
        var embedBuilder = new DiscordEmbedBuilder().WithTitle("Fehler: Datenbank nicht verbunden!")
            .WithDescription(
                $"Command deaktiviert. Bitte informiere den Botentwickler ``{ctx.Client.GetUserAsync(GlobalProperties.BotOwnerId).Result.UsernameWithDiscriminator}``")
            .WithColor(DiscordColor.Red);
        var embed = embedBuilder.Build();
        var msg_e = new DiscordMessageBuilder().WithEmbed(embed).WithReply(ctx.Message.Id);
        await ctx.Channel.SendMessageAsync(msg_e);
        return false;
    }
}

public class RequireTeamCat : CheckBaseAttribute
{
    public override async Task<bool> ExecuteCheckAsync(CommandContext ctx, bool help)
    {
        ulong teamAreaCategoryId = ulong.Parse(GlobalProperties.ConfigIni["ServerConfig"]["TeamAreaCategoryId"]);
        ulong logCategoryId = ulong.Parse(GlobalProperties.ConfigIni["ServerConfig"]["LogCategoryId"]);
        ulong modMailCategoryId = ulong.Parse(GlobalProperties.ConfigIni["ServerConfig"]["ModMailCategoryId"]);

        bool isChannelInValidCategory = ctx.Channel.ParentId == teamAreaCategoryId ||
                                        ctx.Channel.ParentId == logCategoryId ||
                                        ctx.Channel.ParentId == modMailCategoryId;

        return (isChannelInValidCategory);
    }
}