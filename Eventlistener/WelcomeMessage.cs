#region

using AGC_Management.Utils;
using KawaiiAPI.NET;
using KawaiiAPI.NET.Enums;

#endregion

namespace AGC_Management.Eventlistener;

[EventHandler]
public class WelcomeMessage : BaseCommandModule
{
    private readonly KawaiiClient _kawaiiclient;
    private readonly ulong serverid;

    public WelcomeMessage()
    {
        _kawaiiclient = new KawaiiClient();
        serverid = ulong.Parse(BotConfig.GetConfig()["ServerConfig"]["ServerId"]);
    }

    [Event]
    private Task GuildMemberAdded(DiscordClient client, GuildMemberAddEventArgs args)
    {
        _ = Task.Run(async () =>
            {
                if (args.Member.IsBot)
                    return;

                if (args.Guild.Id != serverid) return;

                var active = false;
                ulong channelid = 0;
                try
                {
                    active = bool.Parse(BotConfig.GetConfig()["WelcomeMessage"]["Active"]);
                    channelid = ulong.Parse(BotConfig.GetConfig()["WelcomeMessage"]["ChannelId"]);
                }
                // ReSharper disable once EmptyGeneralCatchClause
                catch (Exception e)
                {
                }

                if (!active) return;

                var embed = new DiscordEmbedBuilder();
                embed.WithTitle($"Hey {args.Member.DisplayName}! Willkommen auf AGC!");
                embed.WithDescription($"__Schau doch hier vorbei!__\n" +
                                      $"┃・<#750365462235316250>\n" +
                                      $"┃・<#752701273043763221>\n" +
                                      $"┃・<#784909775615295508>\n" +
                                      $"┃・<#826083443489636372>\n" +
                                      $"🌟 Unser Café wächst! Aktuell sind wir {args.Guild.MemberCount} begeisterte Mitglieder! ☕\n" +
                                      "Komm und setz dich doch an einen Tisch! ☕");
                embed.WithColor(BotConfig.GetEmbedColor());
                embed.WithFooter("⭐️•°• Wir wünschen einen schönen Aufenthalt! •°•⭐️");
                string? imageurl = null;
                try
                {
                    imageurl = await _kawaiiclient.GetRandomGifAsync(KawaiiGifType.Wave);
                }
                catch (Exception e)
                {
                    CurrentApplication.DiscordClient.Logger.LogError(e, "Error while getting gif from kawaiiapi");
                }

                if (imageurl != null) embed.WithImageUrl(imageurl);

                var channel = await client.GetChannelAsync(channelid);
                if (args.Member.UsernameWithDiscriminator.Contains("chatnoir"))
                    await BanBlacklistedUsers(args.Member.Id, args.Guild,
                        "Blacklisted User | Mitbeteiligter am Epsilon Stealer");

                await Task.Delay(TimeSpan.FromSeconds(5));
                // look if member is still in guild
                var guild = await client.GetGuildAsync(serverid);
                try
                {
                    var member = await guild.GetMemberAsync(args.Member.Id);
                    // ReSharper disable once ConditionIsAlwaysTrueOrFalse
                    if (member == null) return;
                }
                catch (Exception)
                {
                    return;
                }


                await channel.SendMessageAsync(args.Member.Mention, embed);
                await CalcLevel(client, args);
                await AddCosmeticRolesAndLevel0OnJoin(args.Member);
            }
        );
        return Task.CompletedTask;
    }

    private async Task AddCosmeticRolesAndLevel0OnJoin(DiscordMember member)
    {
        List<ulong> roles = new() { 767031029068201994, 767056290148384799, 875679019054551050, 767031210055958538, 750450099871547462 };
        List<DiscordRole> rolesToAdd = new();
        foreach (var role in roles)
        {
            var r = await member.Guild.GetRoleAsync(role);
            if (r != null) rolesToAdd.Add(r);
        }
        if (rolesToAdd.Count > 0)
        {
            foreach (var role in rolesToAdd)
            {
                try
                {
                    await member.GrantRoleAsync(role);
                }
                catch (Exception e)
                {
                    CurrentApplication.DiscordClient.Logger.LogError(e, "Error while adding role to member");
                }
            }
        }
    }


    private async Task CalcLevel(DiscordClient client, GuildMemberAddEventArgs args)
    {
        /*
         * 
         */
        _ = new Task(async () =>
        {
            await LevelUtils.RecalculateUserLevel(args.Member.Id);
            if (args.Guild != GlobalProperties.AGCGuild) return;
            if (args.Guild is not null && args.Member is not null)
            {
                var guild = await client.GetGuildAsync(args.Guild.Id);
                var member = await guild.GetMemberAsync(args.Member.Id);
                await LevelUtils.UpdateLevelRoles(member);
            }
        });
    }


    private async Task BanBlacklistedUsers(ulong UserId, DiscordGuild guild, string reason)
    {
        if (guild.Id != serverid) return;

        await guild.BanMemberAsync(UserId, 0, reason);
    }

    [Command("welcomemessageactive")]
    [RequirePermissions(Permissions.Administrator)]
    public async Task WelcomeMessageActive(CommandContext ctx, bool active)
    {
        BotConfig.SetConfig("WelcomeMessage", "Active", active.ToString());
        await ctx.RespondAsync($"WelcomeMessage wurde auf ``{active}`` gesetzt!");
    }

    [Command("welcomemessagechannel")]
    [RequirePermissions(Permissions.Administrator)]
    public async Task WelcomeMessageChannel(CommandContext ctx, ulong channelid)
    {
        BotConfig.SetConfig("WelcomeMessage", "ChannelId", channelid.ToString());
        await ctx.RespondAsync($"WelcomeMessage wurde auf <#{channelid}> gesetzt!");
    }
}