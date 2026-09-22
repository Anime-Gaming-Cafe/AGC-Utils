#region

using AGC_Management.Entities.Ticket;

#endregion

namespace AGC_Management.Services;

/// <summary>Read model for the team ticket queue on the dashboard.</summary>
public static class TeamTicketService
{
    private static NpgsqlDataSource Db =>
        CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

    public static async Task<List<TeamTicketRow>> GetOpenTicketsAsync(IReadOnlyCollection<TicketCategory> categories)
    {
        if (categories.Count == 0) return [];

        var rows = new List<TeamTicketRow>();
        await using (var cmd = Db.CreateCommand(
                         "SELECT s.ticket_id, s.tickettype, s.ticket_owner, c.tchannel_id, " +
                         "coalesce(s.opened_at, 0), coalesce(c.last_activity, 0), " +
                         "coalesce(c.claimed, false), coalesce(c.claimed_from, 0) " +
                         "FROM ticketstore s JOIN ticketcache c ON c.ticket_id = s.ticket_id " +
                         "WHERE s.closed = false AND s.tickettype = ANY(@categories) " +
                         "ORDER BY coalesce(s.opened_at, 0)"))
        {
            cmd.Parameters.AddWithValue("categories", categories.Select(c => c.CustomId).ToArray());
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                rows.Add(new TeamTicketRow
                {
                    TicketId = reader.GetString(0),
                    CategoryId = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    OwnerId = (ulong)reader.GetInt64(2),
                    ChannelId = (ulong)reader.GetInt64(3),
                    OpenedAt = reader.GetInt64(4),
                    LastActivity = reader.GetInt64(5),
                    Claimed = reader.GetBoolean(6),
                    ClaimedBy = (ulong)reader.GetInt64(7)
                });
        }

        var guild = CurrentApplication.TargetGuild;
        foreach (var row in rows)
        {
            row.CategoryLabel = categories
                .FirstOrDefault(c => string.Equals(c.CustomId, row.CategoryId, StringComparison.OrdinalIgnoreCase))
                ?.Label ?? row.CategoryId;

            if (guild is null) continue;

            if (guild.Channels.TryGetValue(row.ChannelId, out var channel))
            {
                row.ChannelName = channel.Name;
                row.ChannelUrl = $"https://discord.com/channels/{guild.Id}/{channel.Id}";
            }

            row.OwnerName = NameOf(guild, row.OwnerId) ?? row.OwnerId.ToString();
            if (row.ClaimedBy > 0) row.ClaimedByName = NameOf(guild, row.ClaimedBy) ?? row.ClaimedBy.ToString();
        }

        return rows;
    }

    private static string? NameOf(DiscordGuild guild, ulong userId)
    {
        return guild.Members.TryGetValue(userId, out var member) ? member.DisplayName : null;
    }
}
