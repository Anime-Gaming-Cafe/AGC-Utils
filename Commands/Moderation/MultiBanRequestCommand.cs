#region

using AGC_Management.Attributes;
using AGC_Management.Utils;
using DisCatSharp.Exceptions;
using DisCatSharp.Interactivity.Extensions;

#endregion

namespace AGC_Management.Commands.Moderation;

public sealed class MultiBanRequestCommand : BaseCommandModule
{
    [Command("multibanrequest")]
    [Aliases("multibanreq")]
    [Description("Erstellt einen Multiban-Request")]
    [RequireStaffRole]
    public async Task MultiBanRequest(CommandContext ctx, [RemainingText] string ids_and_reason)
    {
        List<ulong> ids;
        string reason;
        Converter.SeperateIdsAndReason(ids_and_reason, out ids, out reason);
        if (reason == null) reason = await ModerationHelper.BanReasonSelector(ctx);

        if (await ToolSet.CheckForReason(ctx, reason)) return;
        if (await ToolSet.TicketUrlCheck(ctx, reason)) return;
        reason = await ReasonTemplateResolver.Resolve(reason);
        reason = reason.TrimEnd(' ');
        var users_to_ban = new List<DiscordUser>();
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
            .WithTitle("Überprüfe deine Eingabe | Aktion: Multibanrequest")
            .WithFooter(ctx.User.UsernameWithDiscriminator, ctx.User.AvatarUrl)
            .WithDescription($"Bitte überprüfe deine Eingabe und bestätige mit ✅ um fortzufahren.\n\n" +
                             $"__Users:__\n" +
                             $"```{busers_formatted}```\n__Grund:__```{reason}```")
            .WithColor(BotConfig.GetEmbedColor());
        var embed = confirmEmbedBuilder.Build();
        List<DiscordButtonComponent> buttons = new(2)
        {
            new DiscordButtonComponent(ButtonStyle.Success, $"multibanrequest_accept_{caseid}", "Bestätigen"),
            new DiscordButtonComponent(ButtonStyle.Danger, $"multibanrequest_deny_{caseid}", "Abbrechen")
        };
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

        // handle if you cancel request
        if (result.Result.Id == $"multibanrequest_deny_{caseid}")
        {
            await result.Result.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);
            var denyEmbedBuilder = new DiscordEmbedBuilder()
                .WithTitle("Anfrage abgebrochen")
                .WithDescription("Du hast deinen Multibanrequest abgebrochen.")
                .WithFooter(ctx.User.UsernameWithDiscriminator, ctx.User.AvatarUrl)
                .WithColor(DiscordColor.Red);
            var denyEmbed = denyEmbedBuilder.Build();
            var denyMessage = new DiscordMessageBuilder()
                .AddEmbed(denyEmbed).AddComponents(buttons)
                .WithReply(ctx.Message.Id);
            await message.ModifyAsync(denyMessage);
            return;
        }

        // handle if you accept 
        if (result.Result.Id == $"multibanrequest_accept_{caseid}")
        {
            await result.Result.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);
            var now = DateTime.Now.ToString("dd.MM.yyyy - HH:mm");
            var banEmbedBuilder = new DiscordEmbedBuilder()
                .WithTitle($"Du wurdest von {ctx.Guild.Name} gebannt!").WithColor(DiscordColor.Red)
                .WithDescription($"**Begründung:**```{reason}```\n" +
                                 $"**Du möchtest einen Entbannungsantrag stellen?**\n" +
                                 $"Dann kannst du eine Entbannung beim [Entbannungsserver]( {ModerationHelper.GetUnbanURL()} ) beantragenen");
            var banEmbed = banEmbedBuilder.Build();
            var staffrole = ctx.Guild.GetRole(ulong.Parse(BotConfig.GetConfig()["ServerConfig"]["StaffRoleId"]));
            var staffmembers = ctx.Guild.Members
                .Where(x => x.Value.Roles.Any(y => y.Id == GlobalProperties.StaffRoleId))
                .Select(x => x.Value)
                .ToList();
            var staffWithBanPerms =
                staffmembers.Where(x => x.Permissions.HasPermission(Permissions.BanMembers)).ToList();
            var onlineStaffWithBanPerms = staffWithBanPerms
                .Where(member => (member.Presence?.Status ?? UserStatus.Offline) != UserStatus.Offline)
                .Where(member => member.Id != 441192596325531648)
                .ToList();
            var embedBuilder = new DiscordEmbedBuilder()
                .WithTitle("Bannanfrage")
                .WithDescription($"Ban-Anfrage für mehrere Benutzer: {busers_formatted}\n" +
                                 $"```{busers_formatted}```\n__Grund:__```{reason}```" +
                                 $"Bitte warte, während diese Anfrage von jemandem mit Bannberechtigung bestätigt wird <a:loading_agc:1084157150747697203>")
                .WithColor(BotConfig.GetEmbedColor())
                .WithFooter($"{ctx.User.UsernameWithDiscriminator}");

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

            var embed_ = embedBuilder.Build();
            List<DiscordButtonComponent> staffbuttons = new(2)
            {
                new DiscordButtonComponent(ButtonStyle.Success, $"modbanrequest_accept_{caseid}", "Annehmen"),
                new DiscordButtonComponent(ButtonStyle.Danger, $"modbanrequest_deny_{caseid}", "Ablehnen"),
                new DiscordButtonComponent(ButtonStyle.Primary, $"modbanrequest_cancel_{caseid}", $"Abbrechen (nur {ctx.User.UsernameWithDiscriminator})")
            };
            // enable buttons
            staffbuttons.ForEach(x => x.Enable());

            var builder = new DiscordMessageBuilder()
                .AddEmbed(embed_)
                .AddComponents(staffbuttons)
                .WithContent(staffMentionString)
                .WithReply(ctx.Message.Id);

            var interactivity = ctx.Client.GetInteractivity();
            await message.ModifyAsync(builder);

            var pingmsg = await ctx.Channel.SendMessageAsync(staffMentionString);
            await pingmsg.DeleteAsync();

            var staffresult = await interactivity.WaitForButtonAsync(message, interaction =>
            {
                if (interaction.Id == $"modbanrequest_cancel_{caseid}")
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
            staffbuttons.ForEach(x => x.Disable());
            if (staffresult.TimedOut)
            {
                var denyEmbedBuilder = new DiscordEmbedBuilder()
                    .WithTitle("Anfrage abgebrochen")
                    .WithDescription(
                        "Deine Anfrage wurde abgebrochen, da sie nicht innerhalb von 6 Stunden bestätigt wurde.")
                    .WithFooter(ctx.User.UsernameWithDiscriminator, ctx.User.AvatarUrl)
                    .WithColor(DiscordColor.Red);
                var denyEmbed = denyEmbedBuilder.Build();
                var denyMessage = new DiscordMessageBuilder()
                    .AddEmbed(denyEmbed)
                    .WithReply(ctx.Message.Id);
                await message.ModifyAsync(denyMessage);
                return;
            }
            
            if (staffresult.Result.Id == $"modbanrequest_cancel_{caseid}")
            {
                var cancelEmbedBuilder = new DiscordEmbedBuilder()
                    .WithTitle("Anfrage abgebrochen")
                    .WithDescription("Deine Anfrage wurde abgebrochen.")
                    .WithFooter(ctx.User.UsernameWithDiscriminator, ctx.User.AvatarUrl)
                    .WithColor(DiscordColor.Red);
                var cancelEmbed = cancelEmbedBuilder.Build();
                var cancelMessage = new DiscordMessageBuilder()
                    .AddEmbed(cancelEmbed)
                    .WithReply(ctx.Message.Id);
                await message.ModifyAsync(cancelMessage);
                return;
            }

            if (staffresult.Result.Id == $"modbanrequest_deny_{caseid}")
            {
                var denyEmbedBuilder = new DiscordEmbedBuilder()
                    .WithTitle("Anfrage abgelehnt")
                    .WithDescription(
                        $"Deine Anfrage wurde von ``{staffresult.Result.User.UsernameWithDiscriminator}`` abgelehnt.")
                    .WithFooter(ctx.User.UsernameWithDiscriminator, ctx.User.AvatarUrl)
                    .WithColor(DiscordColor.Red);
                var denyEmbed = denyEmbedBuilder.Build();
                var denyMessage = new DiscordMessageBuilder()
                    .AddEmbed(denyEmbed)
                    .WithReply(ctx.Message.Id);
                await message.ModifyAsync(denyMessage);
                return;
            }

            if (staffresult.Result.Id == $"modbanrequest_accept_{caseid}")
            {
                var ReasonString =
                    $"{reason} | Banrequest von Moderator: {ctx.User.UsernameWithDiscriminator} | Approver: {staffresult.Result.User.UsernameWithDiscriminator} | Datum: {DateTime.Now:dd.MM.yyyy - HH:mm}";
                await staffresult.Result.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);
                var disbtn = buttons;
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
                        await user.SendMessageAsync(banEmbed);
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
                }

                string e_string;
                var ec = DiscordColor.Red;
                if (n_users != "")
                {
                    e_string = $"Der Multibanrequest wurde mit Fehlern abgeschlossen.\n" +
                               $"__Grund:__ ```{reason}```\n" +
                               $"__Gebannte User:__\n" +
                               $"```{b_users}```";
                    e_string += $"__Nicht gebannte User:__\n" +
                                $"```{n_users}```\n" +
                                $"Bestätigt von {staffresult.Result.User.UsernameWithDiscriminator}";
                    ec = DiscordColor.Yellow;
                }
                else
                {
                    e_string = $"Der Multibanrequest wurde erfolgreich abgeschlossen.\n" +
                               $"__Grund:__ ```{reason}```\n" +
                               $"__Gebannte User:__\n" +
                               $"```{b_users}```\n" +
                               $"Bestätigt von {staffresult.Result.User.UsernameWithDiscriminator}";
                    ec = DiscordColor.Green;
                }

                var discordEmbedBuilder = new DiscordEmbedBuilder()
                    .WithTitle("Multibanrequest abgeschlossen")
                    .WithDescription(e_string)
                    .WithFooter(ctx.User.UsernameWithDiscriminator, ctx.User.AvatarUrl)
                    .WithColor(ec);
                var discordEmbed = discordEmbedBuilder.Build();
                await message.ModifyAsync(new DiscordMessageBuilder().AddEmbed(discordEmbed));
            }
        }
    }
}
