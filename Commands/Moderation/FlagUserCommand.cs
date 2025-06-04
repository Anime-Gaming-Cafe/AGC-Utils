#region

using AGC_Management.Attributes;
using AGC_Management.Providers;
using AGC_Management.Services;
using AGC_Management.Utils;

#endregion

namespace AGC_Management.Commands.Moderation;

public sealed class FlagUserCommand : BaseCommandModule
{
    [Command("flag")]
    [Description("Flaggt einen Nutzer")]
    [RequireDatabase]
    [RequireStaffRole]
    [RequireTeamCat]
    public async Task FlagUser(CommandContext ctx, DiscordUser user, [RemainingText] string reason)
    {
        if (await ToolSet.CheckForReason(ctx, reason)) return;
        var caseid = ToolSet.GenerateCaseID();
        reason = await ReasonTemplateResolver.Resolve(reason);

        var imgExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp" };
        var imgAttachments = ctx.Message.Attachments
            .Where(att => imgExtensions.Contains(Path.GetExtension(att.Filename).ToLower()))
            .ToList();
        var urls = "";
        if (imgAttachments.Count > 0)
        {
            urls = " ";
            foreach (var attachment in imgAttachments)
            {
                var rndm = new Random();
                var rnd = rndm.Next(1000, 9999);
                var imageBytes = await new HttpClient().GetByteArrayAsync(attachment.Url.ToUri());
                var fileName = $"{caseid}_{rnd}{Path.GetExtension(attachment.Filename).ToLower()}";
                urls += $"\n{ImageStoreProvider.SaveModerativeImage(fileName, imageBytes, ImageStoreType.Flag)}";
            }
        }


        Dictionary<string, object> data = new()
        {
            { "userid", (long)user.Id },
            { "punisherid", (long)ctx.User.Id },
            { "datum", DateTimeOffset.Now.ToUnixTimeSeconds() },
            { "description", reason + urls },
            { "caseid", caseid }
        };
        await DatabaseService.InsertDataIntoTable("flags", data);
        var flaglist = new List<dynamic>();

        List<string> selectedFlags = new()
        {
            "*"
        };

        Dictionary<string, object> whereConditions = new()
        {
            { "userid", (long)user.Id }
        };
        var results =
            await DatabaseService.SelectDataFromTable("flags", selectedFlags, whereConditions);
        foreach (var result in results) flaglist.Add(result);


        var flagcount = flaglist.Count;

        var embed = new DiscordEmbedBuilder()
            .WithTitle("Nutzer geflaggt")
            .WithDescription(
                $"Der Nutzer {user.UsernameWithDiscriminator} `{user.Id}` wurde geflaggt!\n Grund: ```{reason}{urls}```Der User hat nun __{flagcount} Flag(s)__. \nID des Flags: ``{caseid}``")
            .WithColor(BotConfig.GetEmbedColor())
            .WithFooter(ctx.User.UsernameWithDiscriminator, ctx.User.AvatarUrl).Build();
        await ctx.RespondAsync(embed);
    }
}