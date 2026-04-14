#region

using AGC_Management.Attributes;
using AGC_Management.Providers;
using AGC_Management.Services;
using AGC_Management.Utils;
using DisCatSharp.Interactivity.Extensions;

#endregion

namespace AGC_Management.Commands.Moderation;

public sealed class MultiFlagUserCommand : BaseCommandModule
{
    [Command("multiflag")]
    [Description("Flaggt mehrere Nutzer")]
    [RequireDatabase]
    [RequireStaffRole]
    [RequireTeamCat]
    public async Task MultiFlagUser(CommandContext ctx, [RemainingText] string ids_and_reason)
    {
		Converter.SeperateIdsAndReason(ids_and_reason, out List<ulong> ids, out string reason);
		if (await ToolSet.CheckForReason(ctx, reason)) return;
        reason = reason.TrimEnd(' ');
        reason = await ReasonTemplateResolver.Resolve(reason);
        var users_to_flag = new List<DiscordUser>();
        var setids = ids.ToHashSet().ToList();
        if (setids.Count < 2)
        {
            var failsuccessEmbedBuilder = new DiscordEmbedBuilder()
                .WithTitle("Fehler")
                .WithDescription("Du musst mindestens 2 User angeben!")
                .WithFooter(ctx.User.UsernameWithDiscriminator, ctx.User.AvatarUrl)
                .WithColor(DiscordColor.Red);
            var failsuccessEmbed = failsuccessEmbedBuilder.Build();
            var failSuccessMessage = new DiscordMessageBuilder()
                .AddEmbed(failsuccessEmbed)
                .WithReply(ctx.Message.Id);
            await ctx.Channel.SendMessageAsync(failSuccessMessage);
            return;
        }

        foreach (var id in setids)
        {
            var user = await ctx.Client.TryGetUserAsync(id);
            if (user != null) users_to_flag.Add(user);
        }

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
                var __caseid = ToolSet.GenerateCaseID();
                var rndm = new Random();
                var rnd = rndm.Next(1000, 9999);
                var imageBytes = await CurrentApplication.HttpClient.GetByteArrayAsync(attachment.Url.ToUri());
                var fileName = $"{__caseid}_{rnd}{Path.GetExtension(attachment.Filename).ToLower()}";
                urls += $"\n{ImageStoreProvider.SaveModerativeImage(fileName, imageBytes, ImageStoreType.Flag)}";
                imageBytes = null;
            }
        }

        var busers_formatted = string.Join("\n", users_to_flag.Select(buser => buser.UsernameWithDiscriminator));
        var caseid = ToolSet.GenerateCaseID();
        var confirmEmbedBuilder = new DiscordEmbedBuilder()
            .WithTitle("Überprüfe deine Eingabe | Aktion: MultiFlag")
            .WithFooter(ctx.User.UsernameWithDiscriminator, ctx.User.AvatarUrl)
            .WithDescription($"Bitte überprüfe deine Eingabe und bestätige mit ✅ um fortzufahren.\n\n" +
                             $"__Users:__\n" +
                             $"```{busers_formatted}```\n__Grund:__```{reason + urls}```")
            .WithColor(BotConfig.GetEmbedColor());
        var embed = confirmEmbedBuilder.Build();
        List<DiscordButtonComponent> buttons =
		[
			new DiscordButtonComponent(ButtonStyle.Success, $"multiflag_accept_{caseid}", "Bestätigen"),
            new DiscordButtonComponent(ButtonStyle.Danger, $"multiflag_deny_{caseid}", "Abbrechen")
        ];
        var messageBuilder = new DiscordMessageBuilder()
            .AddEmbed(embed)
            .WithReply(ctx.Message.Id)
            .AddComponents(buttons);
        var message = await ctx.Channel.SendMessageAsync(messageBuilder);
        var Interactivity = ctx.Client.GetInteractivity();
        var result = await Interactivity.WaitForButtonAsync(message, ctx.User, TimeSpan.FromMinutes(5));
        buttons.ForEach(x => x.Disable());
        if (result.TimedOut)
        {
            var timeoutEmbedBuilder = new DiscordEmbedBuilder()
                .WithTitle("Timeout")
                .WithDescription("Du hast zu lange gebraucht um zu antworten.")
                .WithFooter(ctx.User.UsernameWithDiscriminator, ctx.User.AvatarUrl)
                .WithColor(DiscordColor.Red);
            var timeoutEmbed = timeoutEmbedBuilder.Build();
            var timeoutMessage = new DiscordMessageBuilder()
                .AddEmbed(timeoutEmbed).AddComponents(buttons)
                .WithReply(ctx.Message.Id);
            await message.ModifyAsync(timeoutMessage);
            return;
        }

        if (result.Result.Id == $"multiflag_deny_{caseid}")
        {
            await result.Result.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);
            var loadingEmbedBuilder = new DiscordEmbedBuilder()
                .WithTitle("MultiFlag abgebrochen")
                .WithFooter(ctx.User.UsernameWithDiscriminator, ctx.User.AvatarUrl)
                .WithDescription("Der MultiFlag wurde abgebrochen.")
                .WithColor(DiscordColor.Red);
            var loadingEmbed = loadingEmbedBuilder.Build();
            var loadingMessage = new DiscordMessageBuilder()
                .AddEmbed(loadingEmbed)
                .WithReply(ctx.Message.Id);
            await message.ModifyAsync(loadingMessage);
            return;
        }

        if (result.Result.Id == $"multiflag_accept_{caseid}")
        {
            var disbtn = buttons;
            await result.Result.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);
            disbtn.ForEach(x => x.Disable());
            var loadingEmbedBuilder = new DiscordEmbedBuilder()
                .WithTitle("Multiflag wird bearbeitet")
                .WithFooter(ctx.User.UsernameWithDiscriminator, ctx.User.AvatarUrl)
                .WithDescription("Der Multiflag wird bearbeitet. Bitte warten...")
                .WithColor(DiscordColor.Yellow);
            var loadingEmbed = loadingEmbedBuilder.Build();
            var loadingMessage = new DiscordMessageBuilder()
                .AddEmbed(loadingEmbed).AddComponents(disbtn)
                .WithReply(ctx.Message.Id);
            await message.ModifyAsync(loadingMessage);
            var for_str = "";
            List<DiscordUser> users_to_flag_obj = [];
            foreach (var id in setids)
            {
                var user = await ctx.Client.GetUserAsync(id);
                if (user != null) users_to_flag_obj.Add(user);
            }

            foreach (var user in users_to_flag_obj)
            {
                var caseid_ = ToolSet.GenerateCaseID();
                caseid_ = $"{caseid}-{caseid_}";
                Dictionary<string, object> data = new()
                {
                    { "userid", (long)user.Id },
                    { "punisherid", (long)ctx.User.Id },
                    { "datum", DateTimeOffset.Now.ToUnixTimeSeconds() },
                    { "description", reason + urls },
                    { "caseid", caseid_ }
                };
                await DatabaseService.InsertDataIntoTable("flags", data);
                var flaglist = new List<dynamic>();

                List<string> selectedFlags =
				[
					"*"
                ];

                Dictionary<string, object> whereConditions = new()
                {
                    { "userid", (long)user.Id }
                };
                var results =
                    await DatabaseService.SelectDataFromTable("flags", selectedFlags, whereConditions);
                foreach (var lresult in results) flaglist.Add(lresult);
                var flagcount = flaglist.Count;
                var stringtoadd =
                    $"{user.UsernameWithDiscriminator} {user.Id} | Case-ID: {caseid_} | {flagcount} Flag(s)\n\n";
                for_str += stringtoadd;
            }

            var e_string = $"Der Multiflag wurde erfolgreich abgeschlossen.\n" +
                           $"__Grund:__ ```{reason + urls}```\n" +
                           $"__Geflaggte User:__\n" +
                           $"```{for_str}```";
            var ec = DiscordColor.Green;
            var embedBuilder = new DiscordEmbedBuilder()
                .WithTitle("Multiflag abgeschlossen")
                .WithDescription(e_string)
                .WithFooter(ctx.User.UsernameWithDiscriminator, ctx.User.AvatarUrl)
                .WithColor(ec);
            var sembed = embedBuilder.Build();
            var smessageBuilder = new DiscordMessageBuilder()
                .AddEmbed(sembed)
                .WithReply(ctx.Message.Id);
            await message.ModifyAsync(smessageBuilder);
        }
    }
}