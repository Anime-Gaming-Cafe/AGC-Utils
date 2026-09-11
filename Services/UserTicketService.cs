#region

using AGC_Management.Entities.Ticket;

#endregion

namespace AGC_Management.Services;

public static class UserTicketService
{
    private static NpgsqlDataSource Db =>
        CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

    /// <summary>
    ///     Tickets the user opened or was added to, open ones first, newest first within each group. Ids are
    ///     compared as text: the ticket tables were created outside this code base, so their id column types
    ///     are not pinned down here.
    /// </summary>
    public static async Task<List<UserTicket>> GetTicketsAsync(ulong userId)
    {
        await using var cmd = Db.CreateCommand(
            "SELECT DISTINCT ON (s.ticket_id) s.ticket_id, s.tickettype, s.closed, s.user_transscript_url, " +
            "c.tchannel_id::text, coalesce(s.opened_at, 0), coalesce(s.closed_at, 0) " +
            "FROM ticketstore s LEFT JOIN ticketcache c ON c.ticket_id = s.ticket_id " +
            "WHERE s.ticket_owner::text = @uid OR @uid = ANY(c.ticket_users::text[]) ORDER BY s.ticket_id");
        cmd.Parameters.AddWithValue("uid", userId.ToString());

        var guildId = SupportGuildId();
        var tickets = new List<UserTicket>();

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var ticket = new UserTicket
            {
                TicketId = reader.GetString(0),
                Type = reader.IsDBNull(1) ? "" : reader.GetString(1),
                IsOpen = !reader.IsDBNull(2) && !reader.GetBoolean(2),
                OpenedAt = reader.GetInt64(5),
                ClosedAt = reader.GetInt64(6),
                TranscriptUrl = reader.IsDBNull(3) || string.IsNullOrWhiteSpace(reader.GetString(3))
                    ? null
                    : reader.GetString(3)
            };

            if (ticket.IsOpen && guildId is not null && !reader.IsDBNull(4) &&
                ulong.TryParse(reader.GetString(4), out var channelId))
            {
                ticket.ChannelUrl = $"https://discord.com/channels/{guildId}/{channelId}";
                if (CurrentApplication.DiscordClient.Guilds.TryGetValue(guildId.Value, out var guild) &&
                    guild.Channels.TryGetValue(channelId, out var channel))
                    ticket.ChannelName = channel.Name;
            }

            tickets.Add(ticket);
        }

        return tickets
            .OrderByDescending(t => t.IsOpen)
            .ThenByDescending(t => t.IsOpen ? t.OpenedAt : Math.Max(t.ClosedAt, t.OpenedAt))
            .ToList();
    }

    public static string? GetSupportLink()
    {
        try
        {
            var link = BotConfig.GetConfig()["TicketConfig"]["SupportLink"];
            return string.IsNullOrWhiteSpace(link) ? null : link;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static ulong? SupportGuildId()
    {
        try
        {
            return ulong.Parse(BotConfig.GetConfig()["TicketConfig"]["SupportGuild"]);
        }
        catch (Exception)
        {
            return CurrentApplication.TargetGuild?.Id;
        }
    }
}
