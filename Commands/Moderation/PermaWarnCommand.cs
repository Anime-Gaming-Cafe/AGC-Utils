﻿#region

using AGC_Management.Attributes;
using AGC_Management.Providers;
using AGC_Management.Services;
using AGC_Management.Utils;
using DisCatSharp.Interactivity.Extensions;

#endregion

namespace AGC_Management.Commands.Moderation;

public sealed class PermaWarnCommand : BaseCommandModule
{
    [Command("permawarn")]
    [Description("Verwarnt einen Nutzer permanent")]
    [RequireDatabase]
    [RequireStaffRole]
    [RequireTeamCat]
    public async Task PermaWarnUser(CommandContext ctx, DiscordUser user, [RemainingText] string reason)
    {
        if (reason == null)
        {
            reason = await ModerationHelper.WarnReasonSelector(ctx);
        }

        if (await ToolSet.CheckForReason(ctx, reason)) return;
        if (await ToolSet.TicketUrlCheck(ctx, reason)) return;
        reason = await ReasonTemplateResolver.Resolve(reason);
        var (warnsToKick, warnsToBan) = await ModerationHelper.GetWarnKickValues();
        var caseid = ToolSet.GenerateCaseID();


        var interactivity = ctx.Client.GetInteractivity();
        var confirmEmbedBuilder = new DiscordEmbedBuilder()
            .WithTitle("Überprüfe deine Eingabe | Aktion: Permanente Verwarnung")
            .WithFooter(ctx.User.UsernameWithDiscriminator, ctx.User.AvatarUrl)
            .WithDescription($"Bitte überprüfe deine Eingabe und bestätige mit ✅ um fortzufahren.\n\n" +
                             $"__Users:__\n" +
                             $"```{user.UsernameWithDiscriminator}```\n__Grund:__```{reason}```")
            .WithColor(BotConfig.GetEmbedColor());
        var embed__ = confirmEmbedBuilder.Build();
        List<DiscordButtonComponent> buttons = new(2)
        {
            new DiscordButtonComponent(ButtonStyle.Secondary, $"warn_accept_{caseid}", "✅"),
            new DiscordButtonComponent(ButtonStyle.Secondary, $"warn_deny_{caseid}", "❌")
        };
        var confirmMessage = new DiscordMessageBuilder()
            .WithEmbed(embed__).AddComponents(buttons).WithReply(ctx.Message.Id);
        var confirm = await ctx.Channel.SendMessageAsync(confirmMessage);
        var interaction = await interactivity.WaitForButtonAsync(confirm, ctx.User, TimeSpan.FromSeconds(60));
        buttons.ForEach(x => x.Disable());
        if (interaction.TimedOut)
        {
            var embed_ = new DiscordMessageBuilder()
                .WithEmbed(confirmEmbedBuilder.WithTitle("Ban abgebrochen")
                    .WithFooter(ctx.User.UsernameWithDiscriminator, ctx.User.AvatarUrl)
                    .WithDescription(
                        "Die Permanente Verwarnung wurde abgebrochen.\n\nGrund: Zeitüberschreitung. <:counting_warning:962007085426556989>")
                    .WithColor(DiscordColor.Red).Build());
            await confirm.ModifyAsync(embed_);
            return;
        }

        if (interaction.Result.Id == $"warn_deny_{caseid}")
        {
            await interaction.Result.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);
            var embed_ = new DiscordMessageBuilder()
                .WithEmbed(confirmEmbedBuilder.WithTitle("Ban abgebrochen")
                    .WithFooter(ctx.User.UsernameWithDiscriminator, ctx.User.AvatarUrl)
                    .WithDescription(
                        "Die Permanente Verwarnung wurde abgebrochen.\n\nGrund: Abgebrochen. <:counting_warning:962007085426556989>")
                    .WithColor(DiscordColor.Red).Build());
            await confirm.ModifyAsync(embed_);
            return;
        }

        if (interaction.Result.Id == $"warn_accept_{caseid}")
        {
            await interaction.Result.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);
            
            var att = ctx.Message.Attachments;
            
            string urls = "";
            
            if (att.Count > 0)
            {
                var imgExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp" };
                var imgAttachments = att
                    .Where(att => imgExtensions.Contains(Path.GetExtension(att.Filename).ToLower()))
                    .ToList();

                if (imgAttachments.Count > 0)
                {
                    urls += " ";
                    foreach (var attachment in imgAttachments)
                    {
                        var rndm = new Random();
                        var rnd = rndm.Next(1000, 9999);
                        var imageBytes = await new HttpClient().GetByteArrayAsync(attachment.Url);
                        var fileName = $"{caseid}_{rnd}{Path.GetExtension(attachment.Filename).ToLower()}";
                        urls += $"\n{ImageStoreProvider.SaveImage(fileName, imageBytes)}";
                    }
                }
            }

            var loadingEmbedBuilder = new DiscordEmbedBuilder()
                .WithTitle("Permanente Verwarnung wird bearbeitet")
                .WithFooter(ctx.User.UsernameWithDiscriminator, ctx.User.AvatarUrl)
                .WithDescription("Die Permanente Verwarnung wird bearbeitet. Bitte warten...")
                .WithColor(DiscordColor.Yellow);
            var loadingEmbed = loadingEmbedBuilder.Build();
            var loadingMessage = new DiscordMessageBuilder()
                .WithEmbed(loadingEmbed).AddComponents(buttons)
                .WithReply(ctx.Message.Id);
            await confirm.ModifyAsync(loadingMessage);

            Dictionary<string, object> data = new()
            {
                { "userid", (long)user.Id },
                { "punisherid", (long)ctx.User.Id },
                { "datum", DateTimeOffset.Now.ToUnixTimeSeconds() },
                { "description", reason + urls},
                { "caseid", caseid },
                { "perma", true }
            };

            var warnlist = new List<dynamic>();

            List<string> selectedWarns = new()
            {
                "*"
            };
            Dictionary<string, object> whereConditions = new()
            {
                { "userid", (long)user.Id }
            };

            List<Dictionary<string, object>> results =
                await DatabaseService.SelectDataFromTable("warns", selectedWarns, whereConditions);
            foreach (var result in results) warnlist.Add(result);


            var warncount = warnlist.Count + 1;

            await DatabaseService.InsertDataIntoTable("warns", data);
            DiscordEmbed uembed =
                await ModerationHelper.GeneratePermaWarnEmbed(ctx, user, ctx.User, warncount, caseid, true, reason);
            string reasonString =
                $"{warncount}. Permanente Verwarnung: {reason} | By Moderator: {ctx.User.UsernameWithDiscriminator} | Datum: {DateTime.Now:dd.MM.yyyy - HH:mm}";
            bool sent;
            try
            {
                await user.SendMessageAsync(uembed);
                sent = true;
            }
            catch (Exception)
            {
                sent = false;
            }

            if (!sent)
            {
                await ToolSet.SendWarnAsChannel(ctx, user, uembed, caseid);
            }

            var dmsent = sent ? "✅" : "⚠️";
            string uAction = "Keine";

            var (KickEnabled, BanEnabled) = await ModerationHelper.UserActioningEnabled();

            if (warncount >= warnsToBan)
                try
                {
                    if (BanEnabled)
                    {
                        await ctx.Guild.BanMemberAsync(user, await ToolSet.GenerateBannDeleteMessageDays(user.Id),
                            reasonString);
                        uAction = "Gebannt";
                    }
                }
                catch (Exception)
                {
                }
            else if (warncount >= warnsToKick)
                try
                {
                    if (KickEnabled)
                    {
                        await ctx.Guild.GetMemberAsync(user.Id).Result.RemoveAsync(reasonString);
                        uAction = "Gekickt";
                    }
                }
                catch (Exception)
                {
                }


            var sembed = new DiscordEmbedBuilder()
                .WithTitle("Nutzer permaverwarnt")
                .WithDescription(
                    $"Der Nutzer {user.UsernameWithDiscriminator} `{user.Id}` wurde permanent verwarnt!\n Grund: ```{reason + urls}```Der User hat nun __{warncount} Verwarnung(en)__. \nUser benachrichtigt: {dmsent} \nSekundäre ausgeführte Aktion: **{uAction}** \nID des Warns: ``{caseid}``")
                .WithColor(BotConfig.GetEmbedColor())
                .WithFooter(ctx.User.UsernameWithDiscriminator, ctx.User.AvatarUrl)
                .Build();
            var embedwithoutbuttons = new DiscordMessageBuilder()
                .WithEmbed(sembed);
            await confirm.ModifyAsync(embedwithoutbuttons);
        }
    }
}