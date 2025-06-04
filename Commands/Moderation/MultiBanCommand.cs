#region

using AGC_Management.Utils;
using DisCatSharp.Exceptions;
using DisCatSharp.Interactivity.Extensions;

#endregion

namespace AGC_Management.Commands.Moderation;

public sealed class MultiBanCommand : BaseCommandModule
{
    [Command("multiban")]
    [Description("Bannt mehrere User gleichzeitig.")]
    [RequirePermissions(Permissions.BanMembers)]
    public async Task MultiBan(CommandContext ctx, [RemainingText] string ids_and_reason)
    {
        List<ulong> ids;
        string reason;
        Converter.SeperateIdsAndReason(ids_and_reason, out ids, out reason);
        if (reason == "") reason = await ModerationHelper.BanReasonSelector(ctx);

        if (await ToolSet.CheckForReason(ctx, reason)) return;
        if (await ToolSet.TicketUrlCheck(ctx, reason)) return;
        reason = await ReasonTemplateResolver.Resolve(reason);
        reason = reason.TrimEnd(' ');
        var users_to_ban = new List<DiscordUser>();
        var reasonString =
            $"{reason} | Von Moderator: {ctx.User.UsernameWithDiscriminator} | Datum: {DateTime.Now:dd.MM.yyyy - HH:mm}";
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
            if (user != null) users_to_ban.Add(user);
        }

        var busers_formatted = string.Join("\n", users_to_ban.Select(buser => buser.UsernameWithDiscriminator));
        var caseid = ToolSet.GenerateCaseID();
        var confirmEmbedBuilder = new DiscordEmbedBuilder()
            .WithTitle("Überprüfe deine Eingabe | Aktion: Multiban")
            .WithFooter(ctx.User.UsernameWithDiscriminator, ctx.User.AvatarUrl)
            .WithDescription($"Bitte überprüfe deine Eingabe und bestätige mit ✅ um fortzufahren.\n\n" +
                             $"__Users:__\n" +
                             $"```{busers_formatted}```\n__Grund:__```{reason}```")
            .WithColor(BotConfig.GetEmbedColor());
        var confirmEmbed = confirmEmbedBuilder.Build();

        var embedBuilder = new DiscordEmbedBuilder()
            .WithTitle($"Du wurdest von {ctx.Guild.Name} gebannt!")
            .WithDescription($"**Begründung:**```{reason}```\n" +
                             $"**Du möchtest einen Entbannungsantrag stellen?**\n" +
                             $"Dann kannst du eine Entbannung beim [Entbannungsserver]({ModerationHelper.GetUnbanURL()}) beantragen")
            .WithColor(DiscordColor.Red);
        var UserEmbed = embedBuilder.Build();
        List<DiscordButtonComponent> buttons = new(2)
        {
            new DiscordButtonComponent(ButtonStyle.Secondary, $"multiban_accept_{caseid}", "✅"),
            new DiscordButtonComponent(ButtonStyle.Secondary, $"multiban_deny_{caseid}", "❌")
        };
        var builder = new DiscordMessageBuilder()
            .AddEmbed(confirmEmbed)
            .AddComponents(buttons)
            .WithReply(ctx.Message.Id);
        var interactivity = ctx.Client.GetInteractivity();
        var message = await ctx.Channel.SendMessageAsync(builder);

        var result = await interactivity.WaitForButtonAsync(message, ctx.User, TimeSpan.FromMinutes(10));
        if (result.TimedOut)
        {
            var embed_ = new DiscordMessageBuilder()
                .AddEmbed(confirmEmbedBuilder.WithTitle("Multiban abgebrochen")
                    .WithFooter(ctx.User.UsernameWithDiscriminator, ctx.User.AvatarUrl)
                    .WithDescription(
                        "Der Multiban wurde abgebrochen.\n\nGrund: Zeitüberschreitung. <:counting_warning:962007085426556989>")
                    .WithColor(DiscordColor.Red).Build());
            await message.ModifyAsync(embed_);
            return;
        }

        if (result.Result.Id == $"multiban_deny_{caseid}")
        {
            await result.Result.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);
            var embed_ = new DiscordMessageBuilder()
                .AddEmbed(confirmEmbedBuilder.WithTitle("Multiban abgebrochen")
                    .WithFooter(ctx.User.UsernameWithDiscriminator, ctx.User.AvatarUrl)
                    .WithDescription(
                        "Der Multiban wurde abgebrochen.\n\nGrund: Abgebrochen. <:counting_warning:962007085426556989>")
                    .WithColor(DiscordColor.Red).Build());
            await message.ModifyAsync(embed_);
            return;
        }

        if (result.Result.Id == $"multiban_accept_{caseid}")
        {
            var disbtn = buttons;
            await result.Result.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);
            disbtn.ForEach(x => x.Disable());
            var loadingEmbedBuilder = new DiscordEmbedBuilder()
                .WithTitle("Multiban wird bearbeitet")
                .WithFooter(ctx.User.UsernameWithDiscriminator, ctx.User.AvatarUrl)
                .WithDescription("Der Multiban wird bearbeitet. Bitte warten...")
                .WithColor(DiscordColor.Yellow);
            var loadingEmbed = loadingEmbedBuilder.Build();
            var loadingMessage = new DiscordMessageBuilder()
                .AddEmbed(loadingEmbed).AddComponents(disbtn)
                .WithReply(ctx.Message.Id);
            await message.ModifyAsync(loadingMessage);


            var b_users = "";
            var n_users = "";
            foreach (var user in users_to_ban)
            {
                var sent = false;
                try
                {
                    await user.SendMessageAsync(UserEmbed);
                    sent = true;
                }
                catch
                {
                    sent = false;
                }

                var semoji = sent ? "<:yes:861266772665040917>" : "<:no:861266772724023296>";
                try
                {
                    await ctx.Guild.BanMemberAsync(user.Id, await ToolSet.GenerateBannDeleteMessageDays(user.Id),
                        reasonString);
                    var dm = sent ? "✅" : "❌";
                    b_users += $"{user.UsernameWithDiscriminator} | DM: {dm}\n";
                    await LoggingUtils.LogGuildBan(user.Id, ctx.User.Id, reason);
                }
                catch (UnauthorizedException)
                {
                    n_users += $"{user.UsernameWithDiscriminator}\n";
                }
            }

            string e_string;
            var ec = DiscordColor.Red;
            if (n_users != "")
            {
                e_string = $"Der Multiban wurde mit Fehlern abgeschlossen.\n" +
                           $"__Grund:__ ```{reason}```\n" +
                           $"__Gebannte User:__\n" +
                           $"```{b_users}```";
                e_string += $"__Nicht gebannte User:__\n" +
                            $"```{n_users}```";
                ec = DiscordColor.Yellow;
            }
            else
            {
                e_string = $"Der Multiban wurde erfolgreich abgeschlossen.\n" +
                           $"__Grund:__ ```{reason}```\n" +
                           $"__Gebannte User:__\n" +
                           $"```{b_users}```";
                ec = DiscordColor.Green;
            }

            var discordEmbedBuilder = new DiscordEmbedBuilder()
                .WithTitle("Multiban abgeschlossen")
                .WithDescription(e_string)
                .WithFooter(ctx.User.UsernameWithDiscriminator, ctx.User.AvatarUrl)
                .WithColor(ec);
            var discordEmbed = discordEmbedBuilder.Build();
            await message.ModifyAsync(new DiscordMessageBuilder().AddEmbed(discordEmbed));
        }
    }
}