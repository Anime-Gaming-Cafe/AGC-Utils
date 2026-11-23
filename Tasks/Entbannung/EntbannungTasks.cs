#region

using System.Text.RegularExpressions;
using AGC_Management.Services;
using AGC_Management.Utils;
using DisCatSharp.Exceptions;

#endregion

namespace AGC_Management.Tasks.Entbannung;

public static class EntbannungTasks
{
    private static readonly Regex AntragNamePattern = new(@"^antrag-\d{4}$", RegexOptions.Compiled);
    private static readonly TimeSpan InactivityThreshold = TimeSpan.FromHours(24);

    public static Task LaunchLoops()
    {
        _ = Loop("Teamrollen-Sync", TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), SyncTeamRolesAsync);
        _ = Loop("Abgelaufene Abstimmungen", TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2),
            CloseFinishedVotesAsync);
        _ = Loop("Abgelaufene Sperren", TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), RemoveExpiredSperrenAsync);
        _ = Loop("Abstimmungsnachrichten", TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), UpdateVoteMessagesAsync);
        _ = Loop("Inaktive Anträge", TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(15), CheckInactiveTicketsAsync);
        return Task.CompletedTask;
    }

    private static async Task Loop(string name, TimeSpan initialDelay, TimeSpan interval,
        Func<DiscordClient, Task> iteration)
    {
        await Task.Delay(initialDelay);
        while (true)
        {
            try
            {
                if (EntbannungConfig.Active && CurrentApplication.DiscordClient is { } client)
                    await iteration(client);
            }
            catch (Exception e)
            {
                CurrentApplication.Logger.Error(e, $"Entbannung: {name} fehlgeschlagen");
            }

            await Task.Delay(interval);
        }
    }

    private static async Task SyncTeamRolesAsync(DiscordClient client)
    {
        var mainGuild = CurrentApplication.TargetGuild;
        var unbanGuildId = EntbannungConfig.ServerId;
        if (mainGuild is null || !MemberCacheService.IsDownloaded(mainGuild.Id) ||
            !MemberCacheService.IsDownloaded(unbanGuildId)) return;

        var unbanGuild = await EntbannungService.GetUnbanGuildAsync(client);
        var unbanTeamRole = unbanGuild.GetRole(EntbannungConfig.TeamRoleId);
        var mainTeamRoleId = EntbannungConfig.MainGuildTeamRoleId;
        var ignored = EntbannungConfig.SyncIgnoredRoleIds;

        foreach (var member in mainGuild.Members.Values.ToList())
        {
            if (member.Roles.Any(r => ignored.Contains(r.Id))) continue;
            if (!unbanGuild.Members.TryGetValue(member.Id, out var unbanMember)) continue;

            var hasMainRole = member.Roles.Any(r => r.Id == mainTeamRoleId);
            var hasUnbanRole = unbanMember.Roles.Any(r => r.Id == unbanTeamRole.Id);

            if (hasMainRole && !hasUnbanRole)
                await unbanMember.GrantRoleAsync(unbanTeamRole, "Team role obtained in the main guild.");
            else if (!hasMainRole && hasUnbanRole)
                await unbanMember.RevokeRoleAsync(unbanTeamRole, "Team role lost in the main guild.");
        }
    }

    private static async Task RemoveExpiredSperrenAsync(DiscordClient client)
    {
        var expired = await EntbannungService.GetExpiredSperrenAsync(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        if (expired.Count == 0) return;

        var guild = await EntbannungService.GetUnbanGuildAsync(client);
        var role = guild.GetRole(EntbannungConfig.SperreRoleId);
        foreach (var userId in expired)
        {
            try
            {
                var member = await guild.GetMemberAsync(userId);
                await member.RevokeRoleAsync(role, "Sperre abgelaufen.");
            }
            catch (NotFoundException)
            {
            }

            await Task.Delay(TimeSpan.FromSeconds(5));
        }

        await EntbannungService.DeleteSperrenAsync(expired);
    }

    private static async Task CloseFinishedVotesAsync(DiscordClient client)
    {
        var finished = await EntbannungService.GetFinishedAbstimmungenAsync(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        if (finished.Count == 0) return;

        var votechannel = await client.GetChannelAsync(EntbannungConfig.AbstimmungsChannelId);
        var teamMemberCount = await EntbannungService.GetTeamMemberCountAsync(client);

        foreach (var vote in finished)
        {
            try
            {
                var antragschannel = await client.GetChannelAsync(vote.ChannelId);
                var embed = EntbannungMessages.VoteFinished(antragschannel, DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    vote.NegativeVotes, vote.PositiveVotes,
                    EntbannungService.GetVoteColor(vote.PositiveVotes, vote.NegativeVotes), teamMemberCount);

                try
                {
                    var message = await votechannel.GetMessageAsync(vote.MessageId);
                    await message.DeleteAsync("Abstimmung abgelaufen.");
                }
                catch (Exception e)
                {
                    CurrentApplication.Logger.Warning(e, "Entbannung: Abstimmungsnachricht konnte nicht gelöscht werden");
                }

                await votechannel.SendMessageAsync(new DiscordMessageBuilder()
                    .WithContent(EntbannungService.GetTeamPing())
                    .AddEmbed(embed));
            }
            catch (Exception e)
            {
                CurrentApplication.Logger.Error(e, "Entbannung: Abstimmung {ChannelId} konnte nicht beendet werden",
                    vote.ChannelId);
            }

            await EntbannungService.DeleteAbstimmungAsync(vote);
        }
    }

    private static async Task UpdateVoteMessagesAsync(DiscordClient client)
    {
        var active = await EntbannungService.GetActiveAbstimmungenAsync(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        foreach (var vote in active) await UpdateVoteMessageAsync(client, vote.MessageId);
    }

    public static async Task UpdateVoteMessageAsync(DiscordClient client, ulong messageId)
    {
        try
        {
            var vote = await EntbannungService.GetAbstimmungByMessageAsync(messageId);
            if (vote is null) return;

            var votechannel = await client.GetChannelAsync(EntbannungConfig.AbstimmungsChannelId);
            var message = await votechannel.GetMessageAsync(messageId);
            var antragchannel = await client.GetChannelAsync(vote.ChannelId);

            var embed = EntbannungMessages.VoteRunning(antragchannel, vote.ExpiresAt, vote.NegativeVotes,
                vote.PositiveVotes, EntbannungService.GetVoteColor(vote.PositiveVotes, vote.NegativeVotes),
                await EntbannungService.GetTeamMemberCountAsync(client));

            await message.ModifyAsync(new DiscordMessageBuilder()
                .WithContent(EntbannungService.GetTeamPing())
                .AddComponents(EntbannungMessages.VoteButtons(vote.ChannelId))
                .AddEmbed(embed));
        }
        catch (Exception e)
        {
            CurrentApplication.Logger.Warning(e, "Entbannung: Abstimmungsnachricht {MessageId} nicht aktualisiert",
                messageId);
        }
    }

    private static async Task CheckInactiveTicketsAsync(DiscordClient client)
    {
        var guild = await EntbannungService.GetUnbanGuildAsync(client);
        var now = DateTimeOffset.UtcNow;

        foreach (var channel in guild.Channels.Values.Where(c => AntragNamePattern.IsMatch(c.Name)).ToList())
        {
            try
            {
                if (channel.ParentId == EntbannungConfig.VoteCategoryId) continue;
                if (await EntbannungService.IsAutomationDisabledAsync(channel.Id)) continue;

                var lastMsg = (await channel.GetMessagesAsync(1))?.FirstOrDefault();
                if (lastMsg is null) continue;

                var authorIsTeamOrBot = lastMsg.Author.IsBot;
                if (!authorIsTeamOrBot)
                    try
                    {
                        var author = await guild.GetMemberAsync(lastMsg.Author.Id);
                        authorIsTeamOrBot = author.Roles.Any(r => r.Id == EntbannungConfig.TeamRoleId);
                    }
                    catch
                    {
                    }

                if (!authorIsTeamOrBot) continue;
                if (now - lastMsg.CreationTimestamp < InactivityThreshold) continue;

                var antragsnummer = channel.Name.Split('-').Last();
                var userId = await EntbannungService.GetTicketUserIdAsync(channel);
                if (userId is null)
                {
                    CurrentApplication.Logger.Warning(
                        "Entbannung: UserId konnte nicht aus Channel {ChannelId} gelesen werden", channel.Id);
                    continue;
                }

                if (await EntbannungService.GetSperreAsync(userId.Value) is not null) continue;
                if (await EntbannungService.AntragExistsAsync(antragsnummer, userId.Value)) continue;

                await AutoDeny24Async(client, guild, channel, userId.Value, antragsnummer);
            }
            catch (Exception e)
            {
                CurrentApplication.Logger.Error(e, "Entbannung: Channel {ChannelId} konnte nicht geprüft werden",
                    channel.Id);
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }

    private static async Task AutoDeny24Async(DiscordClient client, DiscordGuild guild, DiscordChannel channel,
        ulong userId, string antragsnummer)
    {
        const string reasonText = "Keine Response nach 24h";
        var expiresAt = DateTimeOffset.UtcNow.AddMonths(3).ToUnixTimeSeconds();

        await EntbannungService.AddSperreAsync(userId, reasonText, expiresAt);

        try
        {
            var member = await guild.GetMemberAsync(userId);
            await member.GrantRoleAsync(guild.GetRole(EntbannungConfig.SperreRoleId),
                "Auto-Antragssperre (24h Inaktivität)");
        }
        catch (Exception e)
        {
            CurrentApplication.Logger.Warning(e, "Entbannung: Sperre-Rolle für {UserId} nicht gesetzt", userId);
        }

        try
        {
            await channel.SendMessageAsync("deny24");
        }
        catch (Exception e)
        {
            CurrentApplication.Logger.Warning(e, "Entbannung: deny24 in {ChannelId} nicht gesendet", channel.Id);
        }

        var targetUser = await client.GetUserAsync(userId);
        await EntbannungService.TryAddAntragsverlaufAsync(client, false, client.CurrentUser, antragsnummer,
            targetUser, reasonText);

        try
        {
            await guild.GetChannel(EntbannungConfig.SperreInfoChannelId).SendMessageAsync(new DiscordEmbedBuilder()
                .WithDescription(
                    $"<t:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}:F> - <@{userId}> ({userId}) - Antrag {antragsnummer} - ``{reasonText}`` -> Gesperrt bis: <t:{expiresAt}:f> ( <t:{expiresAt}:R> )")
                .WithFooter("Automatisch durch 24h-Inaktivitäts-Watcher", client.CurrentUser.AvatarUrl)
                .WithColor(DiscordColor.Orange));
        }
        catch (Exception e)
        {
            CurrentApplication.Logger.Warning(e, "Entbannung: Log-Embed im SperreInfoChannel nicht gepostet");
        }

        try
        {
            await channel.ModifyAsync(x => x.Parent = guild.GetChannel(EntbannungConfig.BearbeitetCategoryId));
        }
        catch (Exception e)
        {
            CurrentApplication.Logger.Warning(e, "Entbannung: {ChannelId} nicht in Bearbeitet verschoben", channel.Id);
        }

        CurrentApplication.Logger.Information(
            "Entbannung: Auto-deny24 für User {UserId} in Channel {ChannelId}", userId, channel.Id);
    }
}
