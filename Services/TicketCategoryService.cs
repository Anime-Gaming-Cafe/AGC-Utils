#region

using System.Collections.Concurrent;
using AGC_Management.Entities.Ticket;
using AGC_Management.Utils;

#endregion

namespace AGC_Management.Services;

/// <summary>
///     Reads and writes the ticket category registry. Categories are hit on every ticket interaction,
///     so the full set is cached for a short while, the same way <see cref="RuntimeSettings" /> does it.
/// </summary>
public static class TicketCategoryService
{
    public const string SettingsSection = "Tickets";

    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
    private static readonly SemaphoreSlim LoadLock = new(1, 1);

    private static List<TicketCategory>? _cache;
    private static DateTime _cacheExpires = DateTime.MinValue;

    private static NpgsqlDataSource Db =>
        CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

    public static void Invalidate()
    {
        _cacheExpires = DateTime.MinValue;
    }

    public static async Task<List<TicketCategory>> GetAllAsync(bool includeDisabled = false)
    {
        var all = await LoadAsync();
        return [.. all.Where(c => includeDisabled || c.Enabled).OrderBy(c => c.SortOrder).ThenBy(c => c.Label)];
    }

    public static async Task<TicketCategory?> GetAsync(string? customId)
    {
        if (string.IsNullOrWhiteSpace(customId)) return null;
        var all = await LoadAsync();
        return all.FirstOrDefault(c => string.Equals(c.CustomId, customId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The category of the ticket living in this channel, or null if it is not a ticket.</summary>
    public static async Task<TicketCategory?> GetForChannelAsync(ulong channelId)
    {
        await using var cmd = Db.CreateCommand(
            "SELECT s.tickettype FROM ticketcache c JOIN ticketstore s ON s.ticket_id = c.ticket_id " +
            "WHERE c.tchannel_id = @channel LIMIT 1");
        cmd.Parameters.AddWithValue("channel", (long)channelId);
        var result = await cmd.ExecuteScalarAsync();
        return await GetAsync(result as string);
    }

    public static async Task<TicketCategory?> GetForTicketAsync(string ticketId)
    {
        await using var cmd = Db.CreateCommand("SELECT tickettype FROM ticketstore WHERE ticket_id = @id LIMIT 1");
        cmd.Parameters.AddWithValue("id", ticketId);
        var result = await cmd.ExecuteScalarAsync();
        return await GetAsync(result as string);
    }

    private static async Task<List<TicketCategory>> LoadAsync()
    {
        if (_cache is not null && _cacheExpires > DateTime.UtcNow) return _cache;

        await LoadLock.WaitAsync();
        try
        {
            if (_cache is not null && _cacheExpires > DateTime.UtcNow) return _cache;

            var categories = new List<TicketCategory>();
            await using (var cmd = Db.CreateCommand(
                             "SELECT custom_id, category_text, description, emoji, channel_prefix, discord_category_id, " +
                             "handler_role_ids, ping_role_ids, welcome_text, intake_enabled, max_open_per_user, " +
                             "autoclose_enabled, autoclose_reminder_hours, autoclose_hours, sort_order, enabled " +
                             "FROM ticketcategories ORDER BY sort_order, custom_id"))
            {
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    categories.Add(new TicketCategory
                    {
                        CustomId = reader.GetString(0),
                        Label = reader.IsDBNull(1) ? reader.GetString(0) : reader.GetString(1),
                        Description = reader.IsDBNull(2) ? "" : reader.GetString(2),
                        Emoji = reader.IsDBNull(3) || string.IsNullOrWhiteSpace(reader.GetString(3))
                            ? null
                            : reader.GetString(3),
                        ChannelPrefix = reader.IsDBNull(4) || string.IsNullOrWhiteSpace(reader.GetString(4))
                            ? reader.GetString(0)
                            : reader.GetString(4),
                        DiscordCategoryId = reader.IsDBNull(5) ? 0 : (ulong)reader.GetInt64(5),
                        HandlerRoleIds = ReadRoleList(reader, 6),
                        PingRoleIds = ReadRoleList(reader, 7),
                        WelcomeText = reader.IsDBNull(8) ? "" : reader.GetString(8),
                        IntakeEnabled = !reader.IsDBNull(9) && reader.GetBoolean(9),
                        MaxOpenPerUser = reader.IsDBNull(10) ? 1 : Math.Max(1, reader.GetInt32(10)),
                        AutoCloseEnabled = !reader.IsDBNull(11) && reader.GetBoolean(11),
                        AutoCloseReminderHours = reader.IsDBNull(12) ? 0 : reader.GetInt32(12),
                        AutoCloseHours = reader.IsDBNull(13) ? 0 : reader.GetInt32(13),
                        SortOrder = reader.IsDBNull(14) ? 0 : reader.GetInt32(14),
                        Enabled = reader.IsDBNull(15) || reader.GetBoolean(15)
                    });
            }

            var questions = await LoadAllQuestionsAsync();
            foreach (var category in categories)
                if (questions.TryGetValue(category.CustomId, out var own))
                    category.Questions = own;

            _cache = categories;
            _cacheExpires = DateTime.UtcNow + CacheTtl;
            return categories;
        }
        finally
        {
            LoadLock.Release();
        }
    }

    private static List<ulong> ReadRoleList(NpgsqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal)) return [];
        var raw = (long[])reader.GetValue(ordinal);
        return [.. raw.Where(id => id > 0).Select(id => (ulong)id)];
    }

    private static async Task<Dictionary<string, List<TicketCategoryQuestion>>> LoadAllQuestionsAsync()
    {
        var result = new Dictionary<string, List<TicketCategoryQuestion>>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = Db.CreateCommand(
            "SELECT id, category_id, position, label, placeholder, style, required, min_length, max_length " +
            "FROM ticket_category_questions ORDER BY category_id, position");
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var question = new TicketCategoryQuestion
            {
                Id = reader.GetString(0),
                CategoryId = reader.GetString(1),
                Position = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                Label = reader.IsDBNull(3) ? "" : reader.GetString(3),
                Placeholder = reader.IsDBNull(4) ? "" : reader.GetString(4),
                Style = reader.IsDBNull(5) ? "short" : reader.GetString(5),
                Required = reader.IsDBNull(6) || reader.GetBoolean(6),
                MinLength = reader.IsDBNull(7) ? 0 : reader.GetInt32(7),
                MaxLength = reader.IsDBNull(8) ? 0 : reader.GetInt32(8)
            };

            if (!result.TryGetValue(question.CategoryId, out var list))
            {
                list = [];
                result[question.CategoryId] = list;
            }

            list.Add(question);
        }

        return result;
    }

    /// <summary>
    ///     Every Discord category that can hold ticket channels, including the configured fallback. Message
    ///     listeners use this as a cheap pre-filter so they do not hit the database for every message in
    ///     the guild.
    /// </summary>
    public static async Task<HashSet<ulong>> GetTicketParentIdsAsync()
    {
        var categories = await LoadAsync();
        var ids = categories.Select(c => c.DiscordCategoryId).Where(id => id > 0).ToHashSet();

        try
        {
            if (ulong.TryParse(BotConfig.GetConfig()["TicketConfig"]["SupportCategoryId"], out var fallback) &&
                fallback > 0)
                ids.Add(fallback);
        }
        catch (Exception)
        {
            // no fallback category configured
        }

        return ids;
    }

    public static async Task<bool> ExistsAsync(string customId)
    {
        await using var cmd = Db.CreateCommand("SELECT COUNT(*) FROM ticketcategories WHERE custom_id = @id");
        cmd.Parameters.AddWithValue("id", customId);
        return await cmd.ExecuteScalarAsync() is long count && count > 0;
    }

    /// <summary>
    ///     Writes a category. Deliberately update-then-insert instead of ON CONFLICT: the unique index on
    ///     custom_id cannot be created on a database that already holds duplicates, and this keeps saving
    ///     from the dashboard working there too.
    /// </summary>
    public static async Task UpsertAsync(TicketCategory category)
    {
        await using (var update = Db.CreateCommand(
                         "UPDATE ticketcategories SET category_text = @label, description = @description, " +
                         "emoji = @emoji, channel_prefix = @prefix, discord_category_id = @category, " +
                         "handler_role_ids = @handlers, ping_role_ids = @pings, welcome_text = @welcome, " +
                         "intake_enabled = @intake, max_open_per_user = @maxopen, autoclose_enabled = @autoclose, " +
                         "autoclose_reminder_hours = @reminder, autoclose_hours = @hours, sort_order = @sort, " +
                         "enabled = @enabled WHERE custom_id = @id"))
        {
            Bind(update, category);
            if (await update.ExecuteNonQueryAsync() > 0)
            {
                Invalidate();
                return;
            }
        }

        await using var insert = Db.CreateCommand(
            "INSERT INTO ticketcategories (custom_id, category_text, description, emoji, channel_prefix, " +
            "discord_category_id, handler_role_ids, ping_role_ids, welcome_text, intake_enabled, max_open_per_user, " +
            "autoclose_enabled, autoclose_reminder_hours, autoclose_hours, sort_order, enabled) " +
            "VALUES (@id, @label, @description, @emoji, @prefix, @category, @handlers, @pings, @welcome, @intake, " +
            "@maxopen, @autoclose, @reminder, @hours, @sort, @enabled)");
        Bind(insert, category);
        await insert.ExecuteNonQueryAsync();

        Invalidate();
    }

    private static void Bind(NpgsqlCommand cmd, TicketCategory category)
    {
        cmd.Parameters.AddWithValue("id", category.CustomId);
        cmd.Parameters.AddWithValue("label", category.Label);
        cmd.Parameters.AddWithValue("description", category.Description ?? "");
        cmd.Parameters.AddWithValue("emoji", (object?)category.Emoji ?? DBNull.Value);
        cmd.Parameters.AddWithValue("prefix", category.ChannelPrefix);
        cmd.Parameters.AddWithValue("category", (long)category.DiscordCategoryId);
        cmd.Parameters.AddWithValue("handlers", category.HandlerRoleIds.Select(id => (long)id).ToArray());
        cmd.Parameters.AddWithValue("pings", category.PingRoleIds.Select(id => (long)id).ToArray());
        cmd.Parameters.AddWithValue("welcome", category.WelcomeText ?? "");
        cmd.Parameters.AddWithValue("intake", category.IntakeEnabled);
        cmd.Parameters.AddWithValue("maxopen", Math.Max(1, category.MaxOpenPerUser));
        cmd.Parameters.AddWithValue("autoclose", category.AutoCloseEnabled);
        cmd.Parameters.AddWithValue("reminder", Math.Max(0, category.AutoCloseReminderHours));
        cmd.Parameters.AddWithValue("hours", Math.Max(0, category.AutoCloseHours));
        cmd.Parameters.AddWithValue("sort", category.SortOrder);
        cmd.Parameters.AddWithValue("enabled", category.Enabled);
    }

    /// <summary>
    ///     Removes a category. Tickets keep their <c>tickettype</c>, so anything already open would lose
    ///     its label. Callers are expected to refuse while open tickets exist.
    /// </summary>
    public static async Task DeleteAsync(string customId)
    {
        await using (var questions = Db.CreateCommand("DELETE FROM ticket_category_questions WHERE category_id = @id"))
        {
            questions.Parameters.AddWithValue("id", customId);
            await questions.ExecuteNonQueryAsync();
        }

        await using var cmd = Db.CreateCommand("DELETE FROM ticketcategories WHERE custom_id = @id");
        cmd.Parameters.AddWithValue("id", customId);
        await cmd.ExecuteNonQueryAsync();

        Invalidate();
    }

    public static async Task<int> CountOpenTicketsAsync(string customId)
    {
        await using var cmd = Db.CreateCommand(
            "SELECT COUNT(*) FROM ticketstore WHERE tickettype = @id AND closed = false");
        cmd.Parameters.AddWithValue("id", customId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public static async Task SaveQuestionsAsync(string categoryId, IReadOnlyList<TicketCategoryQuestion> questions)
    {
        await using (var wipe = Db.CreateCommand("DELETE FROM ticket_category_questions WHERE category_id = @id"))
        {
            wipe.Parameters.AddWithValue("id", categoryId);
            await wipe.ExecuteNonQueryAsync();
        }

        var position = 0;
        foreach (var question in questions.Take(TicketCategoryQuestion.MaxPerCategory))
        {
            if (string.IsNullOrWhiteSpace(question.Label)) continue;

            await using var cmd = Db.CreateCommand(
                "INSERT INTO ticket_category_questions (id, category_id, position, label, placeholder, style, " +
                "required, min_length, max_length) " +
                "VALUES (@id, @category, @position, @label, @placeholder, @style, @required, @min, @max)");
            cmd.Parameters.AddWithValue("id",
                string.IsNullOrWhiteSpace(question.Id) ? ToolSet.GenerateCaseID() : question.Id);
            cmd.Parameters.AddWithValue("category", categoryId);
            cmd.Parameters.AddWithValue("position", position++);
            cmd.Parameters.AddWithValue("label", question.Label.Trim());
            cmd.Parameters.AddWithValue("placeholder", question.Placeholder ?? "");
            cmd.Parameters.AddWithValue("style", question.IsLong ? "long" : "short");
            cmd.Parameters.AddWithValue("required", question.Required);
            cmd.Parameters.AddWithValue("min", Math.Max(0, question.MinLength));
            cmd.Parameters.AddWithValue("max", question.MaxLength > 0 ? question.MaxLength : question.IsLong ? 1000 : 200);
            await cmd.ExecuteNonQueryAsync();
        }

        Invalidate();
    }

    /// <summary>
    ///     Hands out the next channel number for a category. The counter seeds itself from the tickets
    ///     that already exist, so numbering continues where the old COUNT(*) left off.
    /// </summary>
    public static async Task<long> NextTicketNumberAsync(string categoryId)
    {
        await using var cmd = Db.CreateCommand(
            "INSERT INTO ticket_counters (category_id, next_number) " +
            "VALUES (@id, (SELECT COUNT(*) FROM ticketstore WHERE tickettype = @id) + 1) " +
            "ON CONFLICT (category_id) DO UPDATE SET next_number = ticket_counters.next_number + 1 " +
            "RETURNING next_number");
        cmd.Parameters.AddWithValue("id", categoryId);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    public static async Task SaveIntakeAnswersAsync(string ticketId, IReadOnlyList<TicketIntakeAnswer> answers)
    {
        foreach (var answer in answers)
        {
            await using var cmd = Db.CreateCommand(
                "INSERT INTO ticket_intake_answers (ticket_id, question_id, question_label, answer, position) " +
                "VALUES (@ticket, @question, @label, @answer, @position)");
            cmd.Parameters.AddWithValue("ticket", ticketId);
            cmd.Parameters.AddWithValue("question", answer.QuestionId);
            cmd.Parameters.AddWithValue("label", answer.QuestionLabel);
            cmd.Parameters.AddWithValue("answer", answer.Answer);
            cmd.Parameters.AddWithValue("position", answer.Position);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public static async Task LogEventAsync(string ticketId, string eventType, ulong actorId, string? data = null)
    {
        await using var cmd = Db.CreateCommand(
            "INSERT INTO ticket_events (ticket_id, event_type, actor_id, data, timestamp) " +
            "VALUES (@ticket, @type, @actor, @data, @timestamp)");
        cmd.Parameters.AddWithValue("ticket", ticketId);
        cmd.Parameters.AddWithValue("type", eventType);
        cmd.Parameters.AddWithValue("actor", (long)actorId);
        cmd.Parameters.AddWithValue("data", (object?)data ?? DBNull.Value);
        cmd.Parameters.AddWithValue("timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        await cmd.ExecuteNonQueryAsync();
    }
}
