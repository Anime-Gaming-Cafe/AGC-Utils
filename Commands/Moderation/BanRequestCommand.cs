#region

using AGC_Management.Attributes;
using AGC_Management.Utils;
using DisCatSharp.Exceptions;
using DisCatSharp.Interactivity.Extensions;

#endregion

namespace AGC_Management.Commands.Moderation;

public sealed class BanRequestCommand : BaseCommandModule
{
    [Command("banrequest")]
    [Aliases("banreq")]
    [Description("Erstellt einen Banrequest")]
    [RequireStaffRole]
    public async Task BanRequest(CommandContext ctx, DiscordUser user, [RemainingText] string reason)
    {
        if (reason == null) reason = await ModerationHelper.BanReasonSelector(ctx);

        if (await ToolSet.CheckForReason(ctx, reason)) return;
        if (await ToolSet.TicketUrlCheck(ctx, reason)) return;
        reason = await ReasonTemplateResolver.Resolve(reason);
        var caseid = ToolSet.GenerateCaseID();
        var staffrole = ctx.Guild.GetRole(ulong.Parse(BotConfig.GetConfig()["ServerConfig"]["StaffRoleId"]));
        var staffmembers = ctx.Guild.Members
            .Where(x => x.Value.Roles.Any(y => y.Id == GlobalProperties.StaffRoleId))
            .Select(x => x.Value)
            .ToList();
        var staffWithBanPerms = staffmembers.Where(x => x.Permissions.HasPermission(Permissions.BanMembers)).ToList();
        var onlineStaffWithBanPerms = staffWithBanPerms
            .Where(member => (member.Presence?.Status ?? UserStatus.Offline) != UserStatus.Offline).ToList();
        var embedBuilder = new DiscordEmbedBuilder()
            .WithTitle("Bannanfrage")
            .WithDescription($"Ban-Anfrage für Benutzer: ``{user.UsernameWithDiscriminator}`` ``({user.Id})``\n" +
                             $"Banngrund:\n```\n{reason}\n```\n" +
                             $"Bitte warte, während diese Anfrage von jemandem mit Bannberechtigung bestätigt wird <a:loading_agc:1084157150747697203>")
            .WithColor(BotConfig.GetEmbedColor())
            .WithFooter($"{ctx.User.UsernameWithDiscriminator}");
        var interactivity_ = ctx.Client.GetInteractivity();
        var confirmEmbedBuilder = new DiscordEmbedBuilder()
            .WithTitle("Überprüfe deine Eingabe | Aktion: Banrequest")
            .WithFooter(ctx.User.UsernameWithDiscriminator, ctx.User.AvatarUrl)
            .WithDescription($"Bitte überprüfe deine Eingabe und bestätige mit ✅ um fortzufahren.\n\n" +
                             $"__Users:__\n" +
                             $"```{user.UsernameWithDiscriminator}```\n__Grund:__```{reason}```")
            .WithColor(BotConfig.GetEmbedColor());
        var embed__ = confirmEmbedBuilder.Build();
        List<DiscordButtonComponent> buttons_ = new(2)
        {
            new DiscordButtonComponent(ButtonStyle.Secondary, $"br_accept_{caseid}", "✅"),
            new DiscordButtonComponent(ButtonStyle.Secondary, $"br_deny_{caseid}", "❌")
        };
        var confirmMessage = new DiscordMessageBuilder()
            .AddEmbed(embed__).AddComponents(buttons_).WithReply(ctx.Message.Id);
        var confirm = await ctx.Channel.SendMessageAsync(confirmMessage);
        var interaction = await interactivity_.WaitForButtonAsync(confirm, ctx.User, TimeSpan.FromSeconds(60));
        buttons_.ForEach(x => x.Disable());
        if (interaction.TimedOut)
        {
            var embed_ = new DiscordMessageBuilder()
                .AddEmbed(confirmEmbedBuilder.WithTitle("Banrequest abgebrochen")
                    .WithFooter(ctx.User.UsernameWithDiscriminator, ctx.User.AvatarUrl)
                    .WithDescription(
                        "Der Banrequest wurde abgebrochen.\n\nGrund: Zeitüberschreitung. <:counting_warning:962007085426556989>")
                    .WithColor(DiscordColor.Red).Build());
            await confirm.ModifyAsync(embed_);
            return;
        }

        if (interaction.Result.Id == $"br_deny_{caseid}")
        {
            await interaction.Result.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);
            var sembed_ = new DiscordMessageBuilder()
                .AddEmbed(confirmEmbedBuilder.WithTitle("Banrequest abgebrochen")
                    .WithFooter(ctx.User.UsernameWithDiscriminator, ctx.User.AvatarUrl)
                    .WithDescription(
                        "Der Banrequest wurde abgebrochen.\n\nGrund: Abgebrochen. <:counting_warning:962007085426556989>")
                    .WithColor(DiscordColor.Red).Build());
            await confirm.ModifyAsync(sembed_);
            return;
        }

        if (interaction.Result.Id == $"br_accept_{caseid}")
        {
            await interaction.Result.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);
            string staffMentionString;
            if (onlineStaffWithBanPerms.Count > 0)
            {
                if (!GlobalProperties.DebugMode)
                    staffMentionString = string.Join(" ", onlineStaffWithBanPerms
                        .Where(member => member.Id != 441192596325531648)
                        .Select(member => member.Mention));
                else
                    staffMentionString = "DEBUG MODE AKTIV | Kein Ping wird ausgeführt";
            }
            else
            {
                if (!GlobalProperties.DebugMode)
                    staffMentionString =
                        $"Kein Moderator online | <@&{BotConfig.GetConfig()["ServerConfig"]["AdminRoleId"]}> | <@&{BotConfig.GetConfig()["ServerConfig"]["ModRoleId"]}>";
                else
                    staffMentionString = "Kein Moderator online | DEBUG MODE AKTIV";
            }

            var embed = embedBuilder.Build();
            List<DiscordButtonComponent> buttons = new(2)
            {
                new DiscordButtonComponent(ButtonStyle.Success, $"banrequest_accept_{caseid}", "Annehmen"),
                new DiscordButtonComponent(ButtonStyle.Danger, $"banrequest_deny_{caseid}", "Ablehnen"),
                new DiscordButtonComponent(ButtonStyle.Danger, $"banrequest_cancel_{caseid}", $"Abbrechen (nur {ctx.User.UsernameWithDiscriminator})")
            };

            var builder = new DiscordMessageBuilder()
                .AddEmbed(embed)
                .AddComponents(buttons)
                .WithContent(staffMentionString)
                .WithReply(ctx.Message.Id);

            var interactivity = ctx.Client.GetInteractivity();
            var message = await confirm.ModifyAsync(builder);
            try
            {
                var pmsg = await ctx.RespondAsync(staffMentionString);
                await pmsg.DeleteAsync();
            }
            catch (Exception)
            {
                // ignored
            }

            buttons.ForEach(x => x.Disable());
            var result = await interactivity.WaitForButtonAsync(message, interaction =>
            {
                if (interaction.Id == $"banrequest_cancel_{caseid}")
                {
                 
                    if (interaction.User.Id != ctx.User.Id)
                            return false;
                    buttons.ForEach(x => x.Disable());
                    return true;
                    
                }
                    
                if (interaction.User is DiscordMember guildUser)
                    return guildUser.Permissions.HasPermission(Permissions.BanMembers);

                return false;
            }, TimeSpan.FromHours(6));

            if (result.TimedOut)
            {
                var embed_ = new DiscordMessageBuilder()
                    .AddEmbed(embedBuilder.WithTitle("Bannanfrage abgebrochen")
                        .WithDescription(
                            $"Die Bannanfrage für {user} (``{user.Id}``) wurde abgebrochen.\n\nGrund: Zeitüberschreitung. <:counting_warning:962007085426556989>")
                        .WithColor(DiscordColor.Red).Build());
                await message.ModifyAsync(embed_);
                return;
            }
            
            if (result.Result.Id == $"banrequest_cancel_{caseid}")
            {
                await result.Result.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);
                var cancelEmbedBuilder = new DiscordEmbedBuilder()
                    .WithTitle("Bannanfrage abgebrochen")
                    .WithDescription(
                        $"Die Bannanfrage für {user} (``{user.Id}``) wurde abgebrochen.\n\n" +
                        $"Grund: Abgebrochen von `{ctx.User.UsernameWithDiscriminator}`")
                    .WithFooter(ctx.User.UsernameWithDiscriminator, ctx.User.AvatarUrl)
                    .WithColor(DiscordColor.Red);

                var cancelEmbed = cancelEmbedBuilder.Build();
                var CancelMessage = new DiscordMessageBuilder()
                    .AddEmbed(cancelEmbed)
                    .WithReply(ctx.Message.Id);
                await message.ModifyAsync(CancelMessage);
                return;
            }

            if (result.Result.Id == $"banrequest_accept_{caseid}")
            {
                var now = DateTime.Now.ToString("dd.MM.yyyy - HH:mm");
                var banEmbedBuilder = new DiscordEmbedBuilder()
                    .WithTitle($"Du wurdest von {ctx.Guild.Name} gebannt!")
                    .WithDescription($"**Begründung:**```{reason}```\n" +
                                     $"**Du möchtest einen Entbannungsantrag stellen?**\n" +
                                     $"Dann kannst du eine Entbannung beim [Entbannungsserver]({ModerationHelper.GetUnbanURL()}) beantragen")
                    .WithColor(DiscordColor.Red).Build();

                await result.Result.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);
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
                bool sent;
                var ReasonString =
                    $"{reason} | Banrequest von Moderator: {ctx.User.UsernameWithDiscriminator} | Approver: {result.Result.User.UsernameWithDiscriminator} | Datum: {DateTime.Now:dd.MM.yyyy - HH:mm}";
                var ec = DiscordColor.Red;
                DiscordMessage? umsg = null;
                try
                {
                    umsg = await user.SendMessageAsync(banEmbedBuilder);
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
                    e_string = $"Der Ban war nicht erfolgreich!\n" +
                               $"Bestätigt von ``{result.Result.User.UsernameWithDiscriminator}``\n\n" +
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
                               $"Bestätigt von ``{result.Result.User.UsernameWithDiscriminator}``\n\n" +
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
            else if (result.Result.Id == $"banrequest_deny_{caseid}")
            {
                await result.Result.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);
                buttons.ForEach(x => x.Disable());
                var declineEmbedBuilder = new DiscordEmbedBuilder()
                    .WithTitle("Bannanfrage abgebrochen")
                    .WithDescription(
                        $"Die Bannanfrage für {user.UsernameWithDiscriminator} (``{user.Id}``) wurde abgebrochen.\n\n" +
                        $"Grund: Ban wurde abgelehnt von `{result.Result.User.UsernameWithDiscriminator}`")
                    .WithFooter(ctx.User.UsernameWithDiscriminator, ctx.User.AvatarUrl)
                    .WithColor(DiscordColor.Red);

                var declineEmbed = declineEmbedBuilder.Build();
                var DeclineMessage = new DiscordMessageBuilder()
                    .AddEmbed(declineEmbed)
                    .WithReply(ctx.Message.Id);
                await message.ModifyAsync(DeclineMessage);
            }
        }
    }
}
