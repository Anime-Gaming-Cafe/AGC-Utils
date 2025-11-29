#region

using AGC_Management.Utils;
using DisCatSharp.Exceptions;
using DisCatSharp.Interactivity.Extensions;

#endregion

namespace AGC_Management.Commands.Moderation;

public sealed class BanUserCommand : BaseCommandModule
{
    [Command("ban")]
    [Description("Bannt einen User vom Server")]
    [RequirePermissions(Permissions.BanMembers)]
    public async Task BanMember(CommandContext ctx, DiscordUser user, [RemainingText] string reason)
    {
        if (reason == null) reason = await ModerationHelper.BanReasonSelector(ctx);

        if (await ToolSet.CheckForReason(ctx, reason)) return;
        if (await ToolSet.TicketUrlCheck(ctx, reason)) return;

        var staffRole = BotConfig.GetConfig()["ServerConfig"]["StaffRoleId"];
        var guildMember = await ctx.Guild.GetMemberAsync(user.Id);
        if (guildMember.Roles.Any(r => r.Id == ulong.Parse(staffRole)))
        {
            var errorEmbedBuilder = new DiscordEmbedBuilder()
            .WithTitle("Fehler beim Bannen des Users")
                .WithDescription(
                    "Der User kann nicht gebannt werden, da er ein Teammitglied ist. <:counting_warning:962007085426556989>")
                .WithColor(DiscordColor.Red)
                .WithFooter(ctx.User.UsernameWithDiscriminator, ctx.User.AvatarUrl);
            var errorEmbed = errorEmbedBuilder.Build();
            await ctx.Channel.SendMessageAsync(new DiscordMessageBuilder().AddEmbed(errorEmbed));
            return;
        }

        reason = await ReasonTemplateResolver.Resolve(reason);
        var caseid = ToolSet.GenerateCaseID();
        var embedBuilder = new DiscordEmbedBuilder()
            .WithTitle($"Du wurdest von {ctx.Guild.Name} gebannt!")
            .WithDescription($"**Begründung:**```{reason}```\n" +
                             $"**Du möchtest einen Entbannungsantrag stellen?**\n" +
                             $"Dann kannst du eine Entbannung beim [Entbannungsserver]({ModerationHelper.GetUnbanURL()}) beantragen")
            .WithColor(DiscordColor.Red);

        var embed = embedBuilder.Build();
        bool sent;
        var ReasonString =
            $"{reason} | Von Moderator: {ctx.User.UsernameWithDiscriminator} | Datum: {DateTime.Now:dd.MM.yyyy - HH:mm}";
        // abfrage
        var interactivity = ctx.Client.GetInteractivity();
        var confirmEmbedBuilder = new DiscordEmbedBuilder()
            .WithTitle("Überprüfe deine Eingabe | Aktion: Ban")
            .WithFooter(ctx.User.UsernameWithDiscriminator, ctx.User.AvatarUrl)
            .WithDescription($"Bitte überprüfe deine Eingabe und bestätige mit ✅ um fortzufahren.\n\n" +
                             $"__Users:__\n" +
                             $"```{user.UsernameWithDiscriminator}```\n__Grund:__```{reason}```")
            .WithColor(BotConfig.GetEmbedColor());
        var embed__ = confirmEmbedBuilder.Build();
        List<DiscordButtonComponent> buttons = new(2)
        {
            new DiscordButtonComponent(ButtonStyle.Secondary, $"ban_accept_{caseid}", "✅"),
            new DiscordButtonComponent(ButtonStyle.Secondary, $"ban_deny_{caseid}", "❌")
        };
        var confirmMessage = new DiscordMessageBuilder()
            .AddEmbed(embed__).AddComponents(buttons).WithReply(ctx.Message.Id);
        var confirm = await ctx.Channel.SendMessageAsync(confirmMessage);
        var interaction = await interactivity.WaitForButtonAsync(confirm, ctx.User, TimeSpan.FromSeconds(60));
        buttons.ForEach(x => x.Disable());
        if (interaction.TimedOut)
        {
            var embed_ = new DiscordMessageBuilder()
                .AddEmbed(confirmEmbedBuilder.WithTitle("Ban abgebrochen")
                    .WithFooter(ctx.User.UsernameWithDiscriminator, ctx.User.AvatarUrl)
                    .WithDescription(
                        "Der Ban wurde abgebrochen.\n\nGrund: Zeitüberschreitung. <:counting_warning:962007085426556989>")
                    .WithColor(DiscordColor.Red).Build());
            await confirm.ModifyAsync(embed_);
            return;
        }

        if (interaction.Result.Id == $"ban_deny_{caseid}")
        {
            await interaction.Result.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);
            var embed_ = new DiscordMessageBuilder()
                .AddEmbed(confirmEmbedBuilder.WithTitle("Ban abgebrochen")
                    .WithFooter(ctx.User.UsernameWithDiscriminator, ctx.User.AvatarUrl)
                    .WithDescription(
                        "Der Ban wurde abgebrochen.\n\nGrund: Abgebrochen. <:counting_warning:962007085426556989>")
                    .WithColor(DiscordColor.Red).Build());
            await confirm.ModifyAsync(embed_);
            return;
        }

        if (interaction.Result.Id == $"ban_accept_{caseid}")
        {
            await interaction.Result.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);
            var loadingEmbedBuilder = new DiscordEmbedBuilder()
                .WithTitle("Ban wird bearbeitet")
                .WithFooter(ctx.User.UsernameWithDiscriminator, ctx.User.AvatarUrl)
                .WithDescription("Der Ban wird bearbeitet. Bitte warten...")
                .WithColor(DiscordColor.Yellow);
            var loadingEmbed = loadingEmbedBuilder.Build();
            var loadingMessage = new DiscordMessageBuilder()
                .AddEmbed(loadingEmbed).AddComponents(buttons)
                .WithReply(ctx.Message.Id);
            await confirm.ModifyAsync(loadingMessage);

            var b_users = "";
            var n_users = "";
            string e_string;
            var ec = DiscordColor.Red;
            DiscordMessage? umsg = null;
            try
            {
                umsg = await user.SendMessageAsync(embed);
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
                    ReasonString);
                var dm = sent ? "✅" : "❌";
                b_users += $"{user.UsernameWithDiscriminator} | DM: {dm}\n";
                await LoggingUtils.LogGuildBan(user.Id, ctx.User.Id, reason);
            }
            catch (UnauthorizedException)
            {
                n_users += $"{user.UsernameWithDiscriminator}\n";
            }

            if (n_users != "")
            {
                e_string = $"Der Ban war nicht erfolgreich.\n" +
                           $"__Grund:__ ```{reason}```\n";
                e_string += $"__Nicht gebannte User:__\n" +
                            $"```{n_users}```";
                ec = DiscordColor.Red;
                if (sent)
                    try
                    {
                        await umsg.DeleteAsync();
                    }
                    catch (Exception)
                    {
                        // ignored
                    }
            }
            else
            {
                e_string = $"Der Ban wurde erfolgreich abgeschlossen.\n" +
                           $"__Grund:__ ```{reason}```\n" +
                           $"__Gebannte User:__\n" +
                           $"```{b_users}```";
                ec = DiscordColor.Green;
            }

            var discordEmbedBuilder = new DiscordEmbedBuilder()
                .WithTitle("Ban abgeschlossen")
                .WithDescription(e_string)
                .WithFooter(ctx.User.UsernameWithDiscriminator, ctx.User.AvatarUrl)
                .WithColor(ec);
            var discordEmbed = discordEmbedBuilder.Build();
            await confirm.ModifyAsync(new DiscordMessageBuilder().AddEmbed(discordEmbed));
        }
    }
}