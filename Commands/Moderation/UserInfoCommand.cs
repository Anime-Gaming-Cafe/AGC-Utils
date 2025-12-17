#region

using AGC_Management.Attributes;
using AGC_Management.Entities;
using AGC_Management.Services;
using AGC_Management.Utils;
using DisCatSharp.Exceptions;

#endregion

namespace AGC_Management.Commands.Moderation;

public sealed class UserInfoCommand : BaseCommandModule
{
    [Command("userinfo")]
    [RequireDatabase]
    [RequireStaffRole]
    [Description("Zeigt Informationen über einen User an.")]
    [RequireTeamCat]
    public async Task UserInfo(CommandContext ctx, DiscordUser user)
    {
        var isMember = false;
        DiscordMember member = null;
        try
        {
            member = await ctx.Guild.GetMemberAsync(user.Id, true);
            isMember = true;
        }
        catch (NotFoundException)
        {
            isMember = false;
        }

        var ticketcount = "Tickets konnten nicht abgerufen werden.";
        var ticketcount_c = await ToolSet.GetTicketCount(user.Id);
        if (ticketcount_c != null)
            ticketcount = ticketcount_c.ToString();


        var bot_indicator = user.IsBot ? "<:bot:1012035481573265458>" : "";
        var user_status = member?.Presence?.Status.ToString() ?? "Offline";
        var status_indicator = user_status switch
        {
            "Online" => "<:online:1012032516934352986>",
            "Idle" => "<:abwesend:1012032002771406888>",
            "DoNotDisturb" => "<:do_not_disturb:1012031711263064104>",
            "Invisible" or "Offline" => "<:offline:946831431798227056>",
            "Streaming" => "<:twitch_streaming:1012033234080632983>",
            _ => "<:offline:946831431798227056>"
        };
        string platform;
        if (isMember)
        {
            var clientStatus = member?.Presence?.ClientStatus;
            platform = clientStatus switch
            {
                { Desktop: { HasValue: true } } => "User verwendet Discord am Computer",
                { Mobile: { HasValue: true } } => "User verwendet Discord am Handy",
                { Web: { HasValue: true } } => "User verwendet Discord im Browser",
                _ => "Nicht ermittelbar"
            };
        }
        else
        {
            platform = "Nicht ermittelbar. User nicht auf Server";
        }

        var bs_status = false;
        var bs_enabled = false;

        try
        {
            if (GlobalProperties.DebugMode)
                bs_enabled = bool.Parse(BotConfig.GetConfig()["ModHQConfigDBG"]["API_ACCESS_ENABLED"]);
            if (!GlobalProperties.DebugMode)
                bs_enabled = bool.Parse(BotConfig.GetConfig()["ModHQConfig"]["API_ACCESS_ENABLED"]);
        }
        catch (Exception)
        {
            bs_enabled = false;
        }

        var bsflaglist = new List<BannSystemWarn>();
        var bsreportlist = new List<BannSystemReport>();
        if (bs_enabled)
            try
            {
                bsflaglist = await ToolSet.BSWarnToWarn(user);
                bsreportlist = await ToolSet.BSReportToWarn(user);
            }
            catch (Exception)
            {
            }

        bs_status = ToolSet.HasActiveBannSystemReport(bsreportlist);


        var bs_icon = bs_status ? "<:BannSystem:1012006073751830529>" : "";
        if (isMember)
        {
            var Teamler = false;
            var staffuser = ctx.Guild.Members
                .Where(x => x.Value.Roles.Any(y => y.Id == GlobalProperties.StaffRoleId))
                .Select(x => x.Value)
                .ToList();
            string userindicator;
            if (staffuser.Any(x => x.Id == user.Id))
            {
                Teamler = true;
                userindicator = "Teammitglied";
            }
            else
            {
                userindicator = "Mitglied";
            }

            var warnlist = new List<dynamic>();
            var flaglist = new List<dynamic>();
            var permawarnlist = new List<dynamic>();

            var memberID = member.Id;
            List<string> WarnQuery = new()
            {
                "*"
            };
            Dictionary<string, object> warnWhereConditions = new()
            {
                { "perma", false },
                { "userid", (long)memberID }
            };
            var WarnResults =
                await DatabaseService.SelectDataFromTable("warns", WarnQuery, warnWhereConditions);
            foreach (var result in WarnResults) warnlist.Add(result);


            List<string> FlagQuery = new()
            {
                "*"
            };
            Dictionary<string, object> flagWhereConditions = new()
            {
                { "userid", (long)memberID }
            };
            var FlagResults =
                await DatabaseService.SelectDataFromTable("flags", FlagQuery, flagWhereConditions);
            foreach (var result in FlagResults) flaglist.Add(result);


            List<string> pWarnQuery = new()
            {
                "*"
            };
            Dictionary<string, object> pWarnWhereConditions = new()
            {
                { "userid", (long)memberID },
                { "perma", true }
            };
            var pWarnResults =
                await DatabaseService.SelectDataFromTable("warns", pWarnQuery, pWarnWhereConditions);
            foreach (var result in pWarnResults) permawarnlist.Add(result);

            var warncount = warnlist.Count;
            var permawarncount = permawarnlist.Count;

            var booster_icon = member.PremiumSince.HasValue ? "<:Booster:995060205178060960>" : "";
            var timeout_icon = member.IsCommunicationDisabled
                ? "<:timeout:1012038546024059021>"
                : "";
            var vc_icon = member.VoiceState?.Channel != null
                ? "<:voiceuser:1012037037148360815>"
                : "";
            if (member.PremiumSince.HasValue)
                booster_icon = "<:Booster:995060205178060960>";
            else
                booster_icon = "";

            var teamler_ico = Teamler ? "<:staff:1012027870455005357>" : "";
            var warnResults = new List<string>();
            var permawarnResults = new List<string>();
            var flagResults = new List<string>();

            foreach (var flag in flaglist)
            {
                long intValue = flag["punisherid"];
                var ulongValue = (ulong)intValue;
                var puser = await ctx.Client.TryGetUserAsync(ulongValue, false);
                var FlagStr =
                    $"[{(puser != null ? puser.Username : "Unbekannt")}, ``{flag["caseid"]}``]  {Formatter.Timestamp(Converter.ConvertUnixTimestamp(flag["datum"]), TimestampFormat.RelativeTime)}  -  {flag["description"]}";
                flagResults.Add(FlagStr);
            }

            foreach (var bsflag in bsflaglist)
            {
                var pid = bsflag.authorId;
                var puser = await ctx.Client.TryGetUserAsync(pid, false);
                var FlagStr =
                    $"[{(puser != null ? puser.Username : "Unbekannt")}, ``BS-WARN-{bsflag.warnId}``]  {Converter.ConvertUnixTimestamp(bsflag.timestamp).Timestamp()}  -  {bsflag.reason}";
                flagResults.Add(FlagStr);
            }

            foreach (var bsreport in bsreportlist)
            {
                var pid = bsreport.authorId;
                var puser = await ctx.Client.TryGetUserAsync(pid, false);
                var active = bsreport.active;
                var FlagStr =
                    $"[{(puser != null ? puser.Username : "Unbekannt")}, ``BS-REPORT-{bsreport.reportId}{(active ? "" : "-EXPIRED")}``]  {Converter.ConvertUnixTimestamp(bsreport.timestamp).Timestamp()}  -  {bsreport.reason}";
                flagResults.Add(FlagStr);
            }


            var __flagcount = flagResults.Count;

            foreach (var warn in warnlist)
            {
                long intValue = warn["punisherid"];
                var ulongValue = (ulong)intValue;
                var puser = await ctx.Client.TryGetUserAsync(ulongValue, false);
                var FlagStr =
                    $"[{(puser != null ? puser.Username : "Unbekannt")}, ``{warn["caseid"]}``] {Formatter.Timestamp(Converter.ConvertUnixTimestamp(warn["datum"]), TimestampFormat.RelativeTime)} - {warn["description"]}";
                warnResults.Add(FlagStr);
            }

            foreach (var pwarn in permawarnlist)
            {
                long intValue = pwarn["punisherid"];
                var ulongValue = (ulong)intValue;
                var puser = await ctx.Client.TryGetUserAsync(ulongValue, false);
                var FlagStr =
                    $"[{(puser != null ? puser.Username : "Unbekannt")}, ``{pwarn["caseid"]}``] {Formatter.Timestamp(Converter.ConvertUnixTimestamp(pwarn["datum"]), TimestampFormat.RelativeTime)} - {pwarn["description"]}";
                permawarnResults.Add(FlagStr);
            }

            var mcicon = "";
            if (ctx.Guild.Id == 750365461945778209)
                if (member.Roles.Any(x => x.Id == 1121443507425517718))
                    mcicon = "<:minecrafticon:1036687323036926076>";

            // if timeout

            // if booster_seit
            var boost_string = member.PremiumSince.HasValue
                ? $"Boostet seit: {member.PremiumSince.Value.Timestamp()}\n"
                : "";
            // discord native format
            var servernick = member.Nickname != null ? $" \n*Aka. **{member.Nickname}***" : "";
            var userinfostring =
                $"**Das Mitglied**" + $"\n{member.UsernameWithDiscriminator} ``{member.Id}``{servernick}\n" +
                $"{boost_string}\n";
            userinfostring += "**Erstellung, Beitritt und mehr**\n";
            userinfostring += $"**Erstellt:** {member.CreationTimestamp.Timestamp()}\n";
            userinfostring += $"**Beitritt:** {member.JoinedAt.Timestamp()}\n";
            userinfostring +=
                $"**Infobadges:**  {booster_icon} {teamler_ico} {bot_indicator}{vc_icon} {timeout_icon} {mcicon} {bs_icon}\n\n";
            userinfostring += "**Der Online-Status und die Plattform**\n";
            userinfostring += $"{status_indicator} | {platform}\n\n";
            userinfostring += "**Kommunikations-Timeout**\n";
            userinfostring +=
                $"{(member.IsCommunicationDisabled ? $"Nutzer getimeouted bis: {member.CommunicationDisabledUntil.Value.Timestamp()}" : "Nutzer nicht getimeouted")}\n\n";
            userinfostring += "**Anzahl Tickets**\n";
            userinfostring +=
                $"{ticketcount}\n\n";
            userinfostring +=
                $"**Aktueller Voice-Channel**\n{(member.VoiceState != null && member.VoiceState.Channel != null ? member.VoiceState.Channel.Mention : "Mitglied nicht in einem Voice-Channel")}\n\n";
            userinfostring += $"**__Alle Verwarnungen ({warncount})__**\n";
            userinfostring += warnlist.Count == 0
                ? "Es wurden keine gefunden.\n"
                : string.Join("\n\n", warnResults) + "\n";
            userinfostring += $"\n**__Alle Perma-Verwarnungen ({permawarncount})__**\n";
            userinfostring += permawarnlist.Count == 0
                ? "Es wurden keine gefunden.\n"
                : string.Join("\n\n", permawarnResults) + "\n";
            userinfostring += $"\n**__Alle Markierungen ({__flagcount})__**\n";
            userinfostring += __flagcount == 0
                ? "Es wurden keine gefunden.\n"
                : string.Join("\n\n", flagResults) + "\n";


            var embedbuilder = new DiscordEmbedBuilder();
            embedbuilder.WithTitle(
                $"Infos über ein {BotConfig.GetConfig()["ServerConfig"]["ServerNameInitials"]} Mitglied");
            embedbuilder.WithDescription($"Ich konnte folgende Informationen über {userindicator} finden.\n\n" +
                                         userinfostring);
            embedbuilder.WithColor(bs_status ? DiscordColor.Red : BotConfig.GetEmbedColor());
            embedbuilder.WithThumbnail(member.AvatarUrl);
            embedbuilder.WithFooter($"Bericht angefordert von {ctx.User.UsernameWithDiscriminator}",
                ctx.User.AvatarUrl);
            await ctx.RespondAsync(embedbuilder.Build());
        }

        if (!isMember)
        {
            var warnlist = new List<dynamic>();
            var flaglist = new List<dynamic>();
            var permawarnlist = new List<dynamic>();
            var memberID = user.Id;
            List<string> WarnQuery = new()
            {
                "*"
            };
            Dictionary<string, object> warnWhereConditions = new()
            {
                { "perma", false },
                { "userid", (long)memberID }
            };
            var WarnResults =
                await DatabaseService.SelectDataFromTable("warns", WarnQuery, warnWhereConditions);
            foreach (var result in WarnResults) warnlist.Add(result);


            List<string> FlagQuery = new()
            {
                "*"
            };
            Dictionary<string, object> flagWhereConditions = new()
            {
                { "userid", (long)memberID }
            };
            var FlagResults =
                await DatabaseService.SelectDataFromTable("flags", FlagQuery, flagWhereConditions);
            foreach (var result in FlagResults) flaglist.Add(result);

            List<string> pWarnQuery = new()
            {
                "*"
            };
            Dictionary<string, object> pWarnWhereConditions = new()
            {
                { "userid", (long)memberID },
                { "perma", true }
            };
            var pWarnResults =
                await DatabaseService.SelectDataFromTable("warns", pWarnQuery, pWarnWhereConditions);
            foreach (var result in pWarnResults) permawarnlist.Add(result);

            var warncount = warnlist.Count;
            var flagcount = flaglist.Count;
            var permawarncount = permawarnlist.Count;

            var warnResults = new List<string>();
            var permawarnResults = new List<string>();
            var flagResults = new List<string>();

            foreach (var flag in flaglist)
            {
                long intValue = flag["punisherid"];
                var ulongValue = (ulong)intValue;
                var puser = await ctx.Client.TryGetUserAsync(ulongValue, false);
                var FlagStr =
                    $"[{(puser != null ? puser.Username : "Unbekannt")}, ``{flag["caseid"]}``]  {Formatter.Timestamp(Converter.ConvertUnixTimestamp(flag["datum"]), TimestampFormat.RelativeTime)}  -  {flag["description"]}";
                flagResults.Add(FlagStr);
            }

            foreach (var bsflag in bsflaglist)
            {
                var pid = bsflag.authorId;
                var puser = await ctx.Client.TryGetUserAsync(pid, false);
                var FlagStr =
                    $"[{(puser != null ? puser.Username : "Unbekannt")}, ``BS-WARN-{bsflag.warnId}``]  {Converter.ConvertUnixTimestamp(bsflag.timestamp).Timestamp()}  -  {bsflag.reason}";
                flagResults.Add(FlagStr);
            }

            foreach (var bsreport in bsreportlist)
            {
                var pid = bsreport.authorId;
                var puser = await ctx.Client.TryGetUserAsync(pid, false);
                var active = bsreport.active;
                var FlagStr =
                    $"[{(puser != null ? puser.Username : "Unbekannt")}, ``BS-REPORT-{bsreport.reportId}{(active ? "" : "-EXPIRED")}``]  {Converter.ConvertUnixTimestamp(bsreport.timestamp).Timestamp()}  -  {bsreport.reason}";
                flagResults.Add(FlagStr);
            }


            var __flagcount = flagResults.Count;

            foreach (var warn in warnlist)
            {
                long intValue = warn["punisherid"];
                var ulongValue = (ulong)intValue;
                var puser = await ctx.Client.TryGetUserAsync(ulongValue, false);
                var FlagStr =
                    $"[{(puser != null ? puser.Username : "Unbekannt")}, ``{warn["caseid"]}``] {Formatter.Timestamp(Converter.ConvertUnixTimestamp(warn["datum"]), TimestampFormat.RelativeTime)} - {warn["description"]}";
                warnResults.Add(FlagStr);
            }

            foreach (var pwarn in permawarnlist)
            {
                long intValue = pwarn["punisherid"];
                var ulongValue = (ulong)intValue;
                var puser = await ctx.Client.TryGetUserAsync(ulongValue, false);
                var FlagStr =
                    $"[{(puser != null ? puser.Username : "Unbekannt")}, ``{pwarn["caseid"]}``] {Formatter.Timestamp(Converter.ConvertUnixTimestamp(pwarn["datum"]), TimestampFormat.RelativeTime)} - {pwarn["description"]}";
                permawarnResults.Add(FlagStr);
            }

            var isBanned = false;
            string banStatus;
            try
            {
                var ban_entry = await ctx.Guild.GetBanAsync(user.Id);
                banStatus = $"**Nutzer ist Lokal gebannt!** ```{ban_entry.Reason}```";
                isBanned = true;
            }
            catch (NotFoundException)
            {
                banStatus = "Nutzer nicht Lokal gebannt.";
            }
            catch (Exception)
            {
                banStatus = "Ban-Status konnte nicht abgerufen werden.";
            }

            var banicon = isBanned ? "<:banicon:1012003595727671337>" : "";


            var userinfostring =
                $"**Der User**\n{user.UsernameWithDiscriminator} ``{user.Id}``\n\n";
            userinfostring += "**Erstellung, Beitritt und mehr**\n";
            userinfostring += $"**Erstellt:** {user.CreationTimestamp.Timestamp()}\n";
            userinfostring += "**Beitritt:** *User nicht auf dem Server*\n";
            userinfostring += $"**Infobadges:**  {bot_indicator} {bs_icon} {banicon}\n\n";
            userinfostring += "**Der Online-Status und die Plattform**\n";
            userinfostring += $"{status_indicator} | Nicht ermittelbar - User ist nicht auf dem Server\n\n";
            userinfostring += "**Anzahl Tickets**\n";
            userinfostring +=
                $"{ticketcount}\n\n";
            userinfostring += $"**__Alle Verwarnungen ({warncount})__**\n";
            userinfostring += warnlist.Count == 0
                ? "Es wurden keine gefunden.\n"
                : string.Join("\n\n", warnResults) + "\n";
            userinfostring += $"\n**__Alle Perma-Verwarnungen ({permawarncount})__**\n";
            userinfostring += permawarnlist.Count == 0
                ? "Es wurden keine gefunden.\n"
                : string.Join("\n\n", permawarnResults) + "\n";
            userinfostring += $"\n**__Alle Markierungen ({__flagcount})__**\n";
            userinfostring += __flagcount == 0
                ? "Es wurden keine gefunden.\n"
                : string.Join("\n\n", flagResults) + "\n";
            userinfostring += "\n**Lokaler Bannstatus**\n";
            userinfostring += banStatus + "";


            var embedbuilder = new DiscordEmbedBuilder();
            embedbuilder.WithTitle(
                $"Infos über ein {BotConfig.GetConfig()["ServerConfig"]["ServerNameInitials"]} Mitglied");
            embedbuilder.WithDescription("Ich konnte folgende Informationen über den User finden.\n\n" +
                                         userinfostring);
            embedbuilder.WithColor(bs_status ? DiscordColor.Red : BotConfig.GetEmbedColor());
            embedbuilder.WithThumbnail(user.AvatarUrl);
            embedbuilder.WithFooter($"Bericht angefordert von {ctx.User.UsernameWithDiscriminator}",
                ctx.User.AvatarUrl);
            await ctx.RespondAsync(embedbuilder.Build());
        }
    }


    [Command("multiuserinfo")]
    [RequireDatabase]
    [RequireStaffRole]
    [Description("Zeigt Informationen über mehrere User an.")]
    [RequireTeamCat]
    public async Task MultiUserInfo(CommandContext ctx, [RemainingText] string users)
    {
        var usersToCheck = users.Split(' ');
        var uniqueUserIds = new HashSet<ulong>();

        foreach (var member in usersToCheck)
        {
            if (!ulong.TryParse(member, out var memberId)) continue;

            uniqueUserIds.Add(memberId);
        }

        if (uniqueUserIds.Count == 0)
        {
            await ctx.RespondAsync("Keine gültigen User-IDs gefunden.");
            return;
        }

        if (uniqueUserIds.Count > 6)
        {
            await ctx.RespondAsync("Maximal 6 User können gleichzeitig abgefragt werden.");
            return;
        }

        foreach (var memberId in uniqueUserIds)
        {
            //await Task.Delay(1000);
            var us = await ctx.Client.TryGetUserAsync(memberId, false);
            if (us == null) continue;
            await UserInfo(ctx, us);
        }
    }
}