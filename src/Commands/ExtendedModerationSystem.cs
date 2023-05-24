using AGC_Management.Helpers;
using AGC_Management.Services.DatabaseHandler;
using DisCatSharp;
using DisCatSharp.CommandsNext;
using DisCatSharp.CommandsNext.Attributes;
using DisCatSharp.Entities;
using DisCatSharp.Enums;
using DisCatSharp.Exceptions;
using Newtonsoft.Json;
using Npgsql;

namespace AGC_Management.Commands;

public class ExtendedModerationSystem : ModerationSystem
{
    private static async Task<(bool, object, bool)> CheckBannsystem(DiscordUser user)
    {
        using HttpClient client = new();

        string apiKey = GlobalProperties.DebugMode
            ? GlobalProperties.ConfigIni["ModHQConfigDBG"]["API_Key"]
            : GlobalProperties.ConfigIni["ModHQConfig"]["API_Key"];

        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", apiKey);

        string apiUrl = GlobalProperties.DebugMode
            ? GlobalProperties.ConfigIni["ModHQConfigDBG"]["API_URL"]
            : GlobalProperties.ConfigIni["ModHQConfig"]["API_URL"];

        HttpResponseMessage response = await client.GetAsync($"{apiUrl}{user.Id}");

        if (response.IsSuccessStatusCode)
        {
            string json = await response.Content.ReadAsStringAsync();
            dynamic data = JsonConvert.DeserializeObject(json);

            if (data.reports != null && data.reports.Count > 0)
                return (true, data.reports, true);

            return (false, data.reports, true);
        }

        return (false, null, false);
    }


    [Command("userinfo")]
    [RequireDatabase]
    [RequireStaffRole]
    [Description("Zeigt Informationen �ber einen User an.")]
    [RequireTeamCat]
    public async Task UserInfoCommand(CommandContext ctx, DiscordUser user)
    {
        var isMember = false;
        DiscordMember member = null;
        try
        {
            member = await ctx.Guild.GetMemberAsync(user.Id);
            isMember = true;
        }
        catch (NotFoundException)
        {
            isMember = false;
        }

        string bot_indicator = user.IsBot ? "<:bot:1012035481573265458>" : "";
        string user_status = member?.Presence?.Status.ToString() ?? "Offline";
        string status_indicator = user_status switch
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

        bool bs_status = false;
        bool bs_success = false;
        bool bs_enabled = false;

        try
        {
            if (GlobalProperties.DebugMode)
                bs_enabled = bool.Parse(GlobalProperties.ConfigIni["ModHQConfigDBG"]["API_ACCESS_ENABLED"]);
            if (!GlobalProperties.DebugMode)
                bs_enabled = bool.Parse(GlobalProperties.ConfigIni["ModHQConfig"]["API_ACCESS_ENABLED"]);
        }
        catch (Exception)
        {
            bs_enabled = false;
        }

        if (bs_enabled)
            try
            {
                (bool temp_bs_status, object bs, bs_success) = await CheckBannsystem(user);
                bs_status = temp_bs_status;
                if (bs_status)
                    try
                    {
                        DiscordColor clr = DiscordColor.Red;
                        var report_data = (List<object>)bs;
                    }
                    catch (Exception)
                    {
                    }
            }
            catch (Exception)
            {
            }


        string bs_icon = bs_status ? "<:BannSystem:1012006073751830529>" : "";
        if (isMember)
        {
            var Teamler = false;
            List<DiscordMember> staffuser = ctx.Guild.Members
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

            ulong memberID = member.Id;
            NpgsqlConnection conn = DatabaseService.dbConnection;
            List<string> WarnQuery = new()
            {
                "*"
            };
            Dictionary<string, object> warnWhereConditions = new()
            {
                { "perma", false },
                { "userid", (long)memberID }
            };
            List<Dictionary<string, object>> WarnResults =
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
            List<Dictionary<string, object>> FlagResults =
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
            List<Dictionary<string, object>> pWarnResults =
                await DatabaseService.SelectDataFromTable("warns", pWarnQuery, pWarnWhereConditions);
            foreach (var result in pWarnResults) permawarnlist.Add(result);

            int warncount = warnlist.Count;
            int flagcount = flaglist.Count;
            int permawarncount = permawarnlist.Count;

            string booster_icon = member.PremiumSince.HasValue ? "<:Booster:995060205178060960>" : "";
            string timeout_icon = member.CommunicationDisabledUntil.HasValue
                ? "<:timeout:1012036258857226752>"
                : "";
            string vc_icon = member.VoiceState?.Channel != null
                ? "<:voiceuser:1012037037148360815>"
                : "";
            if (member.PremiumSince.HasValue)
                booster_icon = "<:Booster:995060205178060960>";
            else
                booster_icon = "";

            string teamler_ico = Teamler ? "<:staff:1012027870455005357>" : "";
            var warnResults = new List<string>();
            var permawarnResults = new List<string>();
            var flagResults = new List<string>();

            foreach (dynamic flag in flaglist)
            {
                long intValue = flag["punisherid"];
                var ulongValue = (ulong)intValue;
                DiscordUser? puser = await ctx.Client.TryGetUserAsync(ulongValue, false);
                var FlagStr =
                    $"[{(puser != null ? puser.Username : "Unbekannt")}, ``{flag["caseid"]}``]  {Formatter.Timestamp(Converter.ConvertUnixTimestamp(flag["datum"]), TimestampFormat.RelativeTime)}  -  {flag["description"]}";
                flagResults.Add(FlagStr);
            }

            foreach (dynamic warn in warnlist)
            {
                long intValue = warn["punisherid"];
                var ulongValue = (ulong)intValue;
                DiscordUser? puser = await ctx.Client.TryGetUserAsync(ulongValue, false);
                var FlagStr =
                    $"[{(puser != null ? puser.Username : "Unbekannt")}, ``{warn["caseid"]}``] {Formatter.Timestamp(Converter.ConvertUnixTimestamp(warn["datum"]), TimestampFormat.RelativeTime)} - {warn["description"]}";
                warnResults.Add(FlagStr);
            }

            foreach (dynamic pwarn in permawarnlist)
            {
                long intValue = pwarn["punisherid"];
                var ulongValue = (ulong)intValue;
                DiscordUser? puser = await ctx.Client.TryGetUserAsync(ulongValue, false);
                var FlagStr =
                    $"[{(puser != null ? puser.Username : "Unbekannt")}, ``{pwarn["caseid"]}``] {Formatter.Timestamp(Converter.ConvertUnixTimestamp(pwarn["datum"]), TimestampFormat.RelativeTime)} - {pwarn["description"]}";
                permawarnResults.Add(FlagStr);
            }


            // if booster_seit
            string boost_string = member.PremiumSince.HasValue
                ? $"\nBoostet seit: {member.PremiumSince.Value.Timestamp()}"
                : "";
            // discord native format
            var userinfostring =
                $"**Das Mitglied**\n{member.UsernameWithDiscriminator} ``{member.Id}``{boost_string}\n\n";
            userinfostring += "**Erstellung, Beitritt und mehr**\n";
            userinfostring += $"**Erstellt:** {member.CreationTimestamp.Timestamp()}\n";
            userinfostring += $"**Beitritt:** {member.JoinedAt.Timestamp()}\n";
            userinfostring +=
                $"**Infobadges:**  {booster_icon} {teamler_ico} {bot_indicator} {vc_icon} {timeout_icon} {bs_icon}\n\n";
            userinfostring += "**Der Online-Status und die Plattform**\n";
            userinfostring += $"{status_indicator} | {platform}\n\n";
            userinfostring += "**Kommunikations-Timeout**\n";
            userinfostring +=
                $"{(member.CommunicationDisabledUntil.HasValue ? $"Nutzer getimeouted bis: {member.CommunicationDisabledUntil.Value.Timestamp()}" : "Nutzer nicht getimeouted")}\n\n";
            userinfostring +=
                $"**Aktueller Voice-Channel**\n{(member.VoiceState != null && member.VoiceState.Channel != null ? member.VoiceState.Channel.Mention : "Mitglied nicht in einem Voice-Channel")}\n\n";
            userinfostring += $"**Alle Verwarnungen ({warncount})**\n";
            userinfostring += warnlist.Count == 0
                ? "Es wurden keine gefunden.\n"
                : string.Join("\n\n", warnResults) + "\n";
            userinfostring += $"\n**Alle Perma-Verwarnungen ({permawarncount})**\n";
            userinfostring += permawarnlist.Count == 0
                ? "Es wurden keine gefunden.\n"
                : string.Join("\n\n", permawarnResults) + "\n";
            userinfostring += $"\n**Alle Markierungen ({flagcount})**\n";
            userinfostring += flaglist.Count == 0
                ? "Es wurden keine gefunden.\n"
                : string.Join("\n\n", flagResults) + "\n";


            if (bs_success)
            {
                userinfostring += "\n**BannSystem-Status**\n";
                userinfostring += bs_status
                    ? "**__Nutzer ist gemeldet - Siehe BS-Bericht__**"
                    : "Nutzer ist nicht gemeldet";
            }

            var embedbuilder = new DiscordEmbedBuilder();
            embedbuilder.WithTitle($"Infos �ber ein {GlobalProperties.ServerNameInitals} Mitglied");
            embedbuilder.WithDescription($"Ich konnte folgende Informationen �ber {userindicator} finden.\n\n" +
                                         userinfostring);
            embedbuilder.WithColor(bs_status ? DiscordColor.Red : GlobalProperties.EmbedColor);
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
            ulong memberID = user.Id;
            NpgsqlConnection conn = DatabaseService.dbConnection;
            List<string> WarnQuery = new()
            {
                "*"
            };
            Dictionary<string, object> warnWhereConditions = new()
            {
                { "perma", false },
                { "userid", (long)memberID }
            };
            List<Dictionary<string, object>> WarnResults =
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
            List<Dictionary<string, object>> FlagResults =
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
            List<Dictionary<string, object>> pWarnResults =
                await DatabaseService.SelectDataFromTable("warns", pWarnQuery, pWarnWhereConditions);
            foreach (var result in pWarnResults) permawarnlist.Add(result);

            int warncount = warnlist.Count;
            int flagcount = flaglist.Count;
            int permawarncount = permawarnlist.Count;

            var warnResults = new List<string>();
            var permawarnResults = new List<string>();
            var flagResults = new List<string>();

            foreach (dynamic flag in flaglist)
            {
                long intValue = flag["punisherid"];
                var ulongValue = (ulong)intValue;
                DiscordUser? puser = await ctx.Client.TryGetUserAsync(ulongValue, false);
                var FlagStr =
                    $"[{(puser != null ? puser.Username : "Unbekannt")}, ``{flag["caseid"]}``]  {Formatter.Timestamp(Converter.ConvertUnixTimestamp(flag["datum"]), TimestampFormat.RelativeTime)}  -  {flag["description"]}";
                flagResults.Add(FlagStr);
            }

            foreach (dynamic warn in warnlist)
            {
                long intValue = warn["punisherid"];
                var ulongValue = (ulong)intValue;
                DiscordUser? puser = await ctx.Client.TryGetUserAsync(ulongValue, false);
                var FlagStr =
                    $"[{(puser != null ? puser.Username : "Unbekannt")}, ``{warn["caseid"]}``] {Formatter.Timestamp(Converter.ConvertUnixTimestamp(warn["datum"]), TimestampFormat.RelativeTime)} - {warn["description"]}";
                warnResults.Add(FlagStr);
            }

            foreach (dynamic pwarn in permawarnlist)
            {
                long intValue = pwarn["punisherid"];
                var ulongValue = (ulong)intValue;
                DiscordUser? puser = await ctx.Client.TryGetUserAsync(ulongValue, false);
                var FlagStr =
                    $"[{(puser != null ? puser.Username : "Unbekannt")}, ``{pwarn["caseid"]}``] {Formatter.Timestamp(Converter.ConvertUnixTimestamp(pwarn["datum"]), TimestampFormat.RelativeTime)} - {pwarn["description"]}";
                permawarnResults.Add(FlagStr);
            }

            bool isBanned = false;
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

            string banicon = isBanned ? "<:banicon:1012003595727671337>" : "";


            var userinfostring =
                $"**Der User**\n{user.UsernameWithDiscriminator} ``{user.Id}``\n\n";
            userinfostring += "**Erstellung, Beitritt und mehr**\n";
            userinfostring += $"**Erstellt:** {user.CreationTimestamp.Timestamp()}\n";
            userinfostring += "**Beitritt:** *User nicht auf dem Server*\n";
            userinfostring += $"**Infobadges:**  {bot_indicator} {bs_icon} {banicon}\n\n";
            userinfostring += "**Der Online-Status und die Plattform**\n";
            userinfostring += $"{status_indicator} | Nicht ermittelbar - User ist nicht auf dem Server\n\n";
            userinfostring += $"**Alle Verwarnungen ({warncount})**\n";
            userinfostring += warnlist.Count == 0
                ? "Es wurden keine gefunden.\n"
                : string.Join("\n\n", warnResults) + "\n";
            userinfostring += $"\n**Alle Perma-Verwarnungen ({permawarncount})**\n";
            userinfostring += permawarnlist.Count == 0
                ? "Es wurden keine gefunden.\n"
                : string.Join("\n\n", permawarnResults) + "\n";
            userinfostring += $"\n**Alle Markierungen ({flagcount})**\n";
            userinfostring += flaglist.Count == 0
                ? "Es wurden keine gefunden.\n"
                : string.Join("\n\n", flagResults) + "\n";
            userinfostring += "\n**Lokaler Bannstatus**\n";
            userinfostring += banStatus + "";

            if (bs_success)
            {
                userinfostring += "\n**BannSystem-Status**\n";
                userinfostring += bs_status
                    ? "**__Nutzer ist gemeldet - Siehe BS-Bericht__**"
                    : "Nutzer ist nicht gemeldet";
            }

            var embedbuilder = new DiscordEmbedBuilder();
            embedbuilder.WithTitle($"Infos �ber ein {GlobalProperties.ServerNameInitals} Mitglied");
            embedbuilder.WithDescription("Ich konnte folgende Informationen �ber den User finden.\n\n" +
                                         userinfostring);
            embedbuilder.WithColor(bs_status ? DiscordColor.Red : GlobalProperties.EmbedColor);
            embedbuilder.WithThumbnail(user.AvatarUrl);
            embedbuilder.WithFooter($"Bericht angefordert von {ctx.User.UsernameWithDiscriminator}",
                ctx.User.AvatarUrl);
            await ctx.RespondAsync(embedbuilder.Build());
        }
    }

    [Command("multiuserinfo")]
    [RequireDatabase]
    [RequireStaffRole]
    [Description("Zeigt Informationen �ber mehrere User an.")]
    [RequireTeamCat]
    public async Task MultiUserInfo(CommandContext ctx, [RemainingText] string users)
    {
        string[] usersToCheck = users.Split(' ');
        var uniqueUserIds = new HashSet<ulong>();

        foreach (string member in usersToCheck)
        {
            if (!ulong.TryParse(member, out ulong memberId)) continue;

            uniqueUserIds.Add(memberId);
        }

        if (uniqueUserIds.Count == 0)
        {
            await ctx.RespondAsync("Keine g�ltigen User-IDs gefunden.");
            return;
        }

        if (uniqueUserIds.Count > 6)
        {
            await ctx.RespondAsync("Maximal 6 User k�nnen gleichzeitig abgefragt werden.");
            return;
        }

        foreach (ulong memberId in uniqueUserIds)
        {
            //await Task.Delay(1000);
            var us = await ctx.Client.TryGetUserAsync(memberId, false);
            if (us == null) continue;
            await UserInfoCommand(ctx, us);
        }
    }


    [Command("flag")]
    [Description("Flaggt einen Nutzer")]
    [RequireDatabase]
    [RequireStaffRole]
    [RequireTeamCat]
    public async Task FlagUser(CommandContext ctx, DiscordUser user, [RemainingText] string reason)
    {
        if (await Helpers.Helpers.CheckForReason(ctx, reason)) return;
        var caseid = Helpers.Helpers.GenerateCaseID();
        Dictionary<string, object> data = new()
        {
            { "userid", (long)user.Id },
            { "punisherid", (long)ctx.User.Id },
            { "datum", DateTimeOffset.Now.ToUnixTimeSeconds() },
            { "description", reason },
            { "caseid", caseid }
        };
        await DatabaseService.InsertDataIntoTable("flags", data);
        var flaglist = new List<dynamic>();

        List<string> selectedFlags = new()
        {
            "*"
        };
        List<Dictionary<string, object>> results =
            await DatabaseService.SelectDataFromTable("flags", selectedFlags, null);
        foreach (var result in results) flaglist.Add(result);


        var flagcount = flaglist.Count;

        var embed = new DiscordEmbedBuilder()
            .WithTitle("Nutzer geflaggt")
            .WithDescription(
                $"Der Nutzer {user.UsernameWithDiscriminator} `{user.Id}` wurde geflaggt!\n Grund: ```{reason}```Der User hat nun __{flagcount} Flag(s)__. \nDie ID des Flags: ``{caseid}``")
            .WithColor(GlobalProperties.EmbedColor)
            .WithFooter(ctx.User.UsernameWithDiscriminator, ctx.User.AvatarUrl).Build();
        await ctx.RespondAsync(embed);
    }
}