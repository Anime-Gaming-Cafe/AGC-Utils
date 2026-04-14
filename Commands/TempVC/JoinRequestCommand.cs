#region

using AGC_Management.TempVoice;
using AGC_Management.Utils;
using DisCatSharp.Interactivity.Extensions;

#endregion

namespace AGC_Management.Commands.TempVC;

public sealed class JoinRequestCommand : TempVoiceHelper
{
    [Command("joinrequest")]
    [Aliases("joinreq")]
    public async Task JoinRequest(CommandContext ctx, DiscordMember user)
    {
        _ = Task.Run(async () =>
        {
            if (SelfCheck(ctx, user)) return;
            var caseid = ToolSet.GenerateCaseID();
            var db_channels = await GetAllChannelIDsFromDB();
            var userchannel = user.VoiceState?.Channel;
            var userchannelid = userchannel?.Id;
            var channelownerid = await GetChannelOwnerID(user);
            var channel = userchannel;
            DiscordMember? TargetUser = null;
            var msg = await ctx.RespondAsync(
                "<a:loading_agc:1084157150747697203> **Lade...** Versuche eine Beitrittsanfrage zu stellen...");
            if (user.IsBot)
            {
                await msg.ModifyAsync(
                    "<:attention:1085333468688433232> **Fehler!** Dieser User ist ein Bot!");
                return;
            }

            if (userchannel == null)
            {
                await msg.ModifyAsync(
                    "<:attention:1085333468688433232> **Fehler!** Der user ist in keinem Voice Channel.");
                return;
            }

            if (userchannel != null && !db_channels.Contains((long)userchannelid))
            {
                await msg.ModifyAsync(
                    "<:attention:1085333468688433232> **Fehler!** Der User ist nicht in einem Custom Voice Channel.");
                return;
            }

            TargetUser = user;
            if (db_channels.Contains((long)userchannelid) && channelownerid != (long)user.Id)
            {
                var Owner = await ctx.Guild.GetMemberAsync((ulong)channelownerid);
                await msg.ModifyAsync(
                    $"<:attention:1085333468688433232> **Fehler!** Der User ist nicht der Besitzer des Channels. Der Besitzer ist ``{Owner.UsernameWithDiscriminator}`` \nJoinanfrage wird umgeleitet...");
                await Task.Delay(3000);
                TargetUser = Owner;
                if (TargetUser.VoiceState?.Channel == null)
                {
                    await msg.ModifyAsync(
                        "<:attention:1085333468688433232> **Fehler!** Der Besitzer des Channels ist in keinem Voice Channel.");
                    return;
                }


                if (TargetUser.VoiceState?.Channel != null && TargetUser.VoiceState?.Channel.Id != userchannelid)
                {
                    await msg.ModifyAsync(
                        "<:attention:1085333468688433232> **Fehler!** Der Besitzer des Channels ist nicht in dem gewünschten Channel.");
                    return;
                }
            }

            if (db_channels.Contains((long)userchannelid) && channelownerid == (long)TargetUser.Id)
            {
                var ebb = new DiscordEmbedBuilder();
                ebb.WithTitle("Beitrittsanfrage");
                ebb.WithDescription(
                    $"{ctx.Member.UsernameWithDiscriminator} {ctx.Member.Mention} möchte gerne deinem Channel beitreten. Möchtest du die Beitrittsanfage annehmen?\n Du hast 300 Sekunden Zeit");
                ebb.WithFooter($"{ctx.Member.UsernameWithDiscriminator}", ctx.Member.AvatarUrl);
                List<DiscordButtonComponent> buttons =
				[
					new DiscordButtonComponent(ButtonStyle.Success, $"jr_accept_{caseid}", "Ja"),
                    new DiscordButtonComponent(ButtonStyle.Danger, $"jr_deny_{caseid}", "Nein")
                ];
                ebb.WithColor(BotConfig.GetEmbedColor());
                var eb = ebb.Build();
                DiscordMessageBuilder mb = new();
                mb.AddEmbed(eb);
                mb.WithContent($"{TargetUser.Mention}");
                mb.AddComponents(buttons);

                msg = await msg.ModifyAsync(mb);
                var pingmsg = await ctx.RespondAsync($"{TargetUser.Mention}");
                await pingmsg.DeleteAsync();
                var interactivity = ctx.Client.GetInteractivity();
                var channelmods = await RetrieveChannelMods(userchannel);
                var result = await interactivity.WaitForButtonAsync(msg, interaction =>
                {
                    if (interaction.User.Id == TargetUser.Id) return true;

                    ;
                    if (channelmods.Contains(interaction.User.Id))
                    {
                        var buserow = userchannel.PermissionOverwrites
                            .Where(x => x.Type == OverwriteType.Member)
                            .Where(x => x.CheckPermission(Permissions.UseVoice) == PermissionLevel.Denied)
                            .Select(x => x.Id)
                            .ToList();
                        return !buserow.Contains(ctx.User.Id);
                    }

                    return false;
                }, TimeSpan.FromSeconds(300));
                if (!userchannel.Users.Contains(TargetUser) && TargetUser != user)
                {
                    DiscordMessageBuilder msgb = new();
                    msgb.AddEmbed(null);
                    msgb.WithContent(
                        "<:attention:1085333468688433232> **Fehler!** Der User ist nicht mehr in deinem Channel.");
                    await msg.ModifyAsync(msgb);
                    return;
                }

                if (result.TimedOut)
                {
                    DiscordEmbedBuilder eb_ = new();
                    eb_.WithTitle("Beitrittsanfrage abgelaufen");
                    eb_.WithDescription(
                        $"Sorry {ctx.User.Username}, aber {TargetUser.Username} hat nicht in der benötigten Zeit reagiert");
                    eb_.WithFooter($"{ctx.Member.UsernameWithDiscriminator}", ctx.Member.AvatarUrl);
                    eb_.WithColor(DiscordColor.Red);
                    eb_.Build();

                    DiscordMessageBuilder msgb = new();
                    msgb.AddEmbed(eb_);

                    await msg.ModifyAsync(msgb);
                    return;
                }

                if (result.Result.Id == $"jr_accept_{caseid}")
                {
                    await result.Result.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);
                    var invite = await userchannel.CreateInviteAsync(300, 1, false, true);
                    DiscordEmbedBuilder eb_ = new();


                    var overwrites = userchannel.PermissionOverwrites.Select(x => x.ConvertToBuilder()).ToList();
                    overwrites = overwrites.Merge(ctx.Member, Permissions.AccessChannels | Permissions.UseVoice,
                        Permissions.None);
                    var channellimit = userchannel.UserLimit;

                    if (channellimit != 0 && channellimit <= userchannel.Users.Count)
                        channellimit = userchannel.Users.Count + 1;

                    await userchannel.ModifyAsync(x =>
                    {
                        x.PermissionOverwrites = overwrites;
                        x.UserLimit = channellimit;
                    });
                    eb_.WithTitle("Beitrittsanfrage angenommen");
                    eb_.WithDescription(
                        $"Deine Beitrittsanfrage wurde von {result.Result.User.UsernameWithDiscriminator} akzeptiert. Du kannst nun beitreten. \nÜber den Button kannst du dem Kanal beitreten.");
                    eb_.WithFooter($"{ctx.Member.UsernameWithDiscriminator}", ctx.Member.AvatarUrl);
                    eb_.WithColor(BotConfig.GetEmbedColor());
                    eb_.Build();
                    List<DiscordLinkButtonComponent> urlb =
					[
						new DiscordLinkButtonComponent(invite.ToString(), "Kanal betreten")
                    ];
                    DiscordMessageBuilder msgb = new();
                    msgb.AddComponents(urlb);
                    msgb.AddEmbed(eb_);
                    await msg.ModifyAsync(msgb);
                }

                if (result.Result.Id == $"jr_deny_{caseid}")
                {
                    await result.Result.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);
                    DiscordEmbedBuilder eb_ = new();
                    eb_.WithTitle("Beitrittsanfrage abgelehnt");
                    eb_.WithDescription(
                        $"{result.Result.User.UsernameWithDiscriminator} hat deine Beitrittsanfrage abgelehnt.");
                    eb_.WithFooter($"{ctx.Member.UsernameWithDiscriminator}", ctx.Member.AvatarUrl);
                    eb_.WithColor(DiscordColor.Red);
                    eb_.Build();

                    DiscordMessageBuilder msgb = new();
                    msgb.AddEmbed(eb_);
                    await msg.ModifyAsync(msgb);
                }
            }
        });
    }
}