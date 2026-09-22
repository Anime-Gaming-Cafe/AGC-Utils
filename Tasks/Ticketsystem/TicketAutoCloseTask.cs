#region

using AGC_Management.Entities.Ticket;
using AGC_Management.Managers;
using AGC_Management.Services;

#endregion

namespace AGC_Management.Tasks;

/// <summary>
///     Reminds about and eventually closes tickets nobody has written in. Off by default, both globally
///     and per category, so an existing install notices nothing until someone turns it on.
/// </summary>
public static class TicketAutoCloseTask
{
    private static NpgsqlDataSource Db =>
        CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

    public static async Task LaunchLoops()
    {
        await Task.Delay(TimeSpan.FromMinutes(2));
        while (true)
        {
            try
            {
                await RunOnceAsync();
            }
            catch (Exception e)
            {
                CurrentApplication.Logger.Error(e, "Tickets: auto close pass failed");
            }

            await Task.Delay(TimeSpan.FromMinutes(15));
        }
    }

    private static async Task RunOnceAsync()
    {
        if (!await RuntimeSettings.GetBoolAsync(TicketCategoryService.SettingsSection, "AutoCloseEnabled", false))
            return;

        var guild = CurrentApplication.TargetGuild;
        if (guild is null) return;

        var categories = (await TicketCategoryService.GetAllAsync(true))
            .Where(category => category.AutoCloseEnabled &&
                               (category.AutoCloseHours > 0 || category.AutoCloseReminderHours > 0))
            .ToDictionary(category => category.CustomId, StringComparer.OrdinalIgnoreCase);
        if (categories.Count == 0) return;

        foreach (var ticket in await LoadOpenTicketsAsync())
        {
            if (!categories.TryGetValue(ticket.CategoryId, out var category)) continue;

            // Tickets that were already open when activity tracking shipped have no baseline. They are
            // skipped rather than treated as inactive since forever.
            if (ticket.LastActivity <= 0) continue;

            var idleSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - ticket.LastActivity;
            var channel = guild.Channels.TryGetValue(ticket.ChannelId, out var found) ? found : null;
            if (channel is null) continue;

            if (category.AutoCloseHours > 0 && idleSeconds >= category.AutoCloseHours * 3600L)
            {
                await CloseAsync(channel, ticket, category);
                continue;
            }

            if (category.AutoCloseReminderHours > 0 && ticket.ReminderSentAt == 0 &&
                idleSeconds >= category.AutoCloseReminderHours * 3600L)
                await RemindAsync(channel, ticket, category);
        }
    }

    private static async Task RemindAsync(DiscordChannel channel, OpenTicket ticket, TicketCategory category)
    {
        try
        {
            var description = $"<@{ticket.OwnerId}>, in diesem Ticket wurde seit " +
                              $"{category.AutoCloseReminderHours} Stunden nichts mehr geschrieben.";
            if (category.AutoCloseHours > 0)
                description += $"\nWenn weiterhin nichts kommt, wird es nach {category.AutoCloseHours} " +
                               "Stunden Inaktivität automatisch geschlossen.";

            var eb = new DiscordEmbedBuilder()
                .WithTitle("Noch offen?")
                .WithDescription(description)
                .WithColor(DiscordColor.Yellow)
                .WithFooter("AGC-Support-System");

            await channel.SendMessageAsync(new DiscordMessageBuilder()
                .WithContent($"<@{ticket.OwnerId}>").AddEmbed(eb));

            await using var cmd = Db.CreateCommand(
                "UPDATE ticketcache SET reminder_sent_at = @now WHERE ticket_id = @ticket");
            cmd.Parameters.AddWithValue("now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            cmd.Parameters.AddWithValue("ticket", ticket.TicketId);
            await cmd.ExecuteNonQueryAsync();

            await TicketCategoryService.LogEventAsync(ticket.TicketId, "reminded", 0, category.CustomId);
        }
        catch (Exception e)
        {
            CurrentApplication.Logger.Error(e, "Tickets: inactivity reminder for {TicketId} failed", ticket.TicketId);
        }
    }

    private static async Task CloseAsync(DiscordChannel channel, OpenTicket ticket, TicketCategory category)
    {
        try
        {
            await TicketCategoryService.LogEventAsync(ticket.TicketId, "autoclosed", 0, category.CustomId);
            await TicketManager.CloseTicketAsync(channel, CurrentApplication.DiscordClient.CurrentUser,
                $"Automatisch geschlossen nach {category.AutoCloseHours} Stunden ohne Aktivität.");
        }
        catch (Exception e)
        {
            CurrentApplication.Logger.Error(e, "Tickets: auto close of {TicketId} failed", ticket.TicketId);
        }
    }

    private static async Task<List<OpenTicket>> LoadOpenTicketsAsync()
    {
        var tickets = new List<OpenTicket>();
        await using var cmd = Db.CreateCommand(
            "SELECT s.ticket_id, s.tickettype, s.ticket_owner, c.tchannel_id, " +
            "coalesce(c.last_activity, 0), coalesce(c.reminder_sent_at, 0) " +
            "FROM ticketstore s JOIN ticketcache c ON c.ticket_id = s.ticket_id " +
            "WHERE s.closed = false");
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            tickets.Add(new OpenTicket(
                reader.GetString(0),
                reader.IsDBNull(1) ? "" : reader.GetString(1),
                (ulong)reader.GetInt64(2),
                (ulong)reader.GetInt64(3),
                reader.GetInt64(4),
                reader.GetInt64(5)));

        return tickets;
    }

    private readonly record struct OpenTicket(
        string TicketId,
        string CategoryId,
        ulong OwnerId,
        ulong ChannelId,
        long LastActivity,
        long ReminderSentAt);
}
