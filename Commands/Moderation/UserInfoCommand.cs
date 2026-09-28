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
        DiscordMember? member = null;
        try
        {
            member = await ctx.Guild.GetMemberAsync(user.Id, true);
        }
        catch (NotFoundException)
        {
        }

        var isMember = member is not null;

        var ticketCountTask = ToolSet.GetTicketCount(user.Id);
        var lastSeenTask = AvailabilityService.GetLastSeenAsync(user.Id, member);
        var bannSystemTask = ToolSet.GetBannSystemEntries(user.Id);
        var casesTask = LoadCasesAsync(user.Id);
        var extraPermissionsTask = ExtraPermissionService.GetStatusAsync(user.Id, member);
        var banTask = isMember ? Task.FromResult((false, "")) : GetBanStatusAsync(ctx.Guild, user.Id);
        await Task.WhenAll(ticketCountTask, lastSeenTask, bannSystemTask, casesTask, extraPermissionsTask, banTask);

        var ticketcount = ticketCountTask.Result.ToString();
        var lastSeen = lastSeenTask.Result;
        var (bsflaglist, bsreportlist) = bannSystemTask.Result;
        var (warnlist, permawarnlist, flaglist) = casesTask.Result;
        var (isBanned, banStatus) = banTask.Result;

        var authorIds = warnlist.Concat(permawarnlist).Concat(flaglist).Select(c => c.PunisherId)
            .Concat(bsflaglist.Select(w => w.authorId))
            .Concat(bsreportlist.Select(r => r.authorId));
        var names = await ResolveUsernamesAsync(ctx.Client, authorIds);
        string NameOf(ulong id) => names.TryGetValue(id, out var name) ? name : "Unbekannt";

        var bot_indicator = user.IsBot ? "<:bot:1012035481573265458>" : "";
        var showPresence = ctx.Client.Intents.HasIntent(DiscordIntents.GuildPresences);
        var lastSeenText = lastSeen.LastSeenUnix > 0
            ? $"{Formatter.Timestamp(Converter.ConvertUnixTimestamp(lastSeen.LastSeenUnix), TimestampFormat.RelativeTime)} · {AvailabilityService.DescribeSignal(lastSeen.Signal)}"
            : "Keine Aktivität aufgezeichnet";
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

        var bs_status = ToolSet.HasActiveBannSystemReport(bsreportlist);
        var bs_icon = bs_status ? "<:BannSystem:1012006073751830529>" : "";

        var warnResults = warnlist.Select(w =>
            $"[{NameOf(w.PunisherId)}, ``{w.CaseId}``] {Formatter.Timestamp(Converter.ConvertUnixTimestamp(w.Datum), TimestampFormat.RelativeTime)} - {w.Description}").ToList();
        var permawarnResults = permawarnlist.Select(w =>
            $"[{NameOf(w.PunisherId)}, ``{w.CaseId}``] {Formatter.Timestamp(Converter.ConvertUnixTimestamp(w.Datum), TimestampFormat.RelativeTime)} - {w.Description}").ToList();
        var flagResults = flaglist.Select(f =>
                $"[{NameOf(f.PunisherId)}, ``{f.CaseId}``]  {Formatter.Timestamp(Converter.ConvertUnixTimestamp(f.Datum), TimestampFormat.RelativeTime)}  -  {f.Description}")
            .Concat(bsflaglist.Select(w =>
                $"[{NameOf(w.authorId)}, ``BS-WARN-{w.warnId}``]  {Converter.ConvertUnixTimestamp(w.timestamp).Timestamp()}  -  {w.reason}"))
            .Concat(bsreportlist.Select(r =>
                $"[{NameOf(r.authorId)}, ``BS-REPORT-{r.reportId}{(r.active ? "" : "-EXPIRED")}``]  {Converter.ConvertUnixTimestamp(r.timestamp).Timestamp()}  -  {r.reason}"))
            .ToList();

        var casesSection = $"**__Alle Verwarnungen ({warnlist.Count})__**\n";
        casesSection += warnlist.Count == 0
            ? "Es wurden keine gefunden.\n"
            : string.Join("\n\n", warnResults) + "\n";
        casesSection += $"\n**__Alle Perma-Verwarnungen ({permawarnlist.Count})__**\n";
        casesSection += permawarnlist.Count == 0
            ? "Es wurden keine gefunden.\n"
            : string.Join("\n\n", permawarnResults) + "\n";
        casesSection += $"\n**__Alle Markierungen ({flagResults.Count})__**\n";
        casesSection += flagResults.Count == 0
            ? "Es wurden keine gefunden.\n"
            : string.Join("\n\n", flagResults) + "\n";

        var extraPermissionsSection = ExtraPermissionFormatter.BuildUserInfoSection(extraPermissionsTask.Result);

        string userinfostring;
        string description;
        if (member is not null)
        {
            var Teamler = member.Roles.Any(r => r.Id == GlobalProperties.StaffRoleId);
            var userindicator = Teamler ? "Teammitglied" : "Mitglied";
            var clientStatus = member.Presence?.ClientStatus;
            var platform = clientStatus switch
            {
                { Desktop.HasValue: true } => "User verwendet Discord am Computer",
                { Mobile.HasValue: true } => "User verwendet Discord am Handy",
                { Web.HasValue: true } => "User verwendet Discord im Browser",
                _ => "Nicht ermittelbar"
            };

            var booster_icon = member.PremiumSince.HasValue ? "<:Booster:995060205178060960>" : "";
            var timeout_icon = member.IsCommunicationDisabled
                ? "<:timeout:1012038546024059021>"
                : "";
            var vc_icon = member.VoiceState?.Channel != null
                ? "<:voiceuser:1012037037148360815>"
                : "";
            var teamler_ico = Teamler ? "<:staff:1012027870455005357>" : "";

            var boost_string = member.PremiumSince.HasValue
                ? $"Boostet seit: {member.PremiumSince.Value.Timestamp()}\n"
                : "";
            var servernick = member.Nickname != null ? $" \n*Aka. **{member.Nickname}***" : "";
            userinfostring =
                $"**Das Mitglied**" + $"\n{member.GetFormattedUserName()} ``{member.Id}``{servernick}\n" +
                $"{boost_string}\n";
            userinfostring += "**Erstellung, Beitritt und mehr**\n";
            userinfostring += $"**Erstellt:** {member.CreationTimestamp.Timestamp()}\n";
            userinfostring += $"**Beitritt:** {member.JoinedAt.Timestamp()}\n";
            userinfostring +=
                $"**Infobadges:**  {booster_icon} {teamler_ico} {bot_indicator}{vc_icon} {timeout_icon} {bs_icon}\n\n";
            if (showPresence)
            {
                userinfostring += "**Der Online-Status und die Plattform**\n";
                userinfostring += $"{status_indicator} | {platform}\n\n";
            }

            userinfostring += "**Zuletzt gesehen**\n";
            userinfostring += $"{lastSeenText}\n\n";
            userinfostring += "**Kommunikations-Timeout**\n";
            userinfostring +=
                $"{(member.IsCommunicationDisabled ? $"Nutzer getimeouted bis: {member.CommunicationDisabledUntil.Value.Timestamp()}" : "Nutzer nicht getimeouted")}\n\n";
            userinfostring += "**Anzahl Tickets**\n";
            userinfostring +=
                $"{ticketcount}\n\n";
            userinfostring +=
                $"**Aktueller Voice-Channel**\n{(member.VoiceState != null && member.VoiceState.Channel != null ? member.VoiceState.Channel.Mention : "Mitglied nicht in einem Voice-Channel")}\n\n";
            userinfostring += casesSection;
            userinfostring += "\n**__Extra Permissions__**\n";
            userinfostring += extraPermissionsSection;
            description = $"Ich konnte folgende Informationen über {userindicator} finden.\n\n" + userinfostring;
        }
        else
        {
            var banicon = isBanned ? "<:banicon:1012003595727671337>" : "";

            userinfostring =
                $"**Der User**\n{user.GetFormattedUserName()} ``{user.Id}``\n\n";
            userinfostring += "**Erstellung, Beitritt und mehr**\n";
            userinfostring += $"**Erstellt:** {user.CreationTimestamp.Timestamp()}\n";
            userinfostring += "**Beitritt:** *User nicht auf dem Server*\n";
            userinfostring += $"**Infobadges:**  {bot_indicator} {bs_icon} {banicon}\n\n";
            if (showPresence)
            {
                userinfostring += "**Der Online-Status und die Plattform**\n";
                userinfostring += $"{status_indicator} | Nicht ermittelbar - User ist nicht auf dem Server\n\n";
            }

            userinfostring += "**Zuletzt gesehen**\n";
            userinfostring += $"{lastSeenText}\n\n";
            userinfostring += "**Anzahl Tickets**\n";
            userinfostring +=
                $"{ticketcount}\n\n";
            userinfostring += casesSection;
            userinfostring += "\n**Lokaler Bannstatus**\n";
            userinfostring += banStatus;
            userinfostring += "\n\n**__Extra Permissions__**\n";
            userinfostring += extraPermissionsSection;
            description = "Ich konnte folgende Informationen über den User finden.\n\n" + userinfostring;
        }

        await EmbedPaginator.SendPaginatedEmbed(
            ctx,
            $"Infos über ein {BotConfig.GetConfig()["ServerConfig"]["ServerNameInitials"]} Mitglied",
            description,
            bs_status ? DiscordColor.Red : BotConfig.GetEmbedColor(),
            member?.AvatarUrl ?? user.AvatarUrl,
            $"Bericht angefordert von {ctx.User.GetFormattedUserName()}",
            ctx.User.AvatarUrl
        );
    }

    private sealed record CaseEntry(ulong PunisherId, string CaseId, long Datum, string Description);

    private static async Task<(List<CaseEntry> Warns, List<CaseEntry> PermaWarns, List<CaseEntry> Flags)>
        LoadCasesAsync(ulong userId)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = con.CreateCommand(
            "SELECT punisherid, caseid, datum, description, perma FROM warns WHERE userid = @userid " +
            "UNION ALL SELECT punisherid, caseid, datum, description, NULL FROM flags WHERE userid = @userid");
        cmd.Parameters.AddWithValue("userid", (long)userId);

        List<CaseEntry> warns = [], permaWarns = [], flags = [];
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var entry = new CaseEntry(
                (ulong)reader.GetInt64(0),
                reader.IsDBNull(1) ? "" : reader.GetString(1),
                reader.GetInt64(2),
                reader.IsDBNull(3) ? "" : reader.GetString(3));
            if (reader.IsDBNull(4)) flags.Add(entry);
            else if (reader.GetBoolean(4)) permaWarns.Add(entry);
            else warns.Add(entry);
        }

        return (warns, permaWarns, flags);
    }

    private static async Task<Dictionary<ulong, string>> ResolveUsernamesAsync(DiscordClient client,
        IEnumerable<ulong> userIds)
    {
        var lookups = userIds.Distinct().Select(async id => (id, user: await client.TryGetUserAsync(id, false)));
        var users = await Task.WhenAll(lookups);
        return users.Where(u => u.user != null).ToDictionary(u => u.id, u => u.user!.Username);
    }

    private static async Task<(bool IsBanned, string Status)> GetBanStatusAsync(DiscordGuild guild, ulong userId)
    {
        try
        {
            var ban = await guild.GetBanAsync(userId);
            return (true, $"**Nutzer ist Lokal gebannt!** ```{ban.Reason}```");
        }
        catch (NotFoundException)
        {
            return (false, "Nutzer nicht Lokal gebannt.");
        }
        catch (Exception)
        {
            return (false, "Ban-Status konnte nicht abgerufen werden.");
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
            var us = await ctx.Client.TryGetUserAsync(memberId, false);
            if (us == null) continue;
            await UserInfo(ctx, us);
        }
    }
}