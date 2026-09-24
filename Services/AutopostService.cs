#region

using System.Text.Json;
using AGC_Management.Entities.Autoposts;
using AGC_Management.Enums.Autoposts;
using AGC_Management.Utils;
using NpgsqlTypes;

#endregion

namespace AGC_Management.Services;

public static class AutopostService
{
    private const string Columns =
        "autopost_id, name, enabled, channel_id, trigger_type, threshold, count_scope_ids, subtract_leaves, " +
        "interval_minutes, parent_autopost_id, parent_delay_seconds, delete_previous, rotation_mode, steps, " +
        "counter_since, join_count, last_posted_at, last_message_ids, rotation_state, created_at, updated_at";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly SemaphoreSlim Gate = new(1, 1);

    private static NpgsqlDataSource Db => CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

    public record Progress(long Current, long Target, string Text);

    public record SendResult(bool Sent, string Message);

    #region Persistence

    public static async Task<List<Autopost>> GetAllAsync()
    {
        var autoposts = new List<Autopost>();
        await using (var cmd = Db.CreateCommand($"SELECT {Columns} FROM autoposts ORDER BY name"))
        {
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) autoposts.Add(Read(reader));
        }

        if (autoposts.Count == 0) return autoposts;

        var byId = autoposts.ToDictionary(a => a.AutopostId);
        await using (var cmd = Db.CreateCommand(
                         "SELECT condition_id, autopost_id, condition_type, negate, params, created_at FROM autopost_conditions ORDER BY created_at"))
        {
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var condition = ReadCondition(reader);
                if (byId.TryGetValue(condition.AutopostId, out var owner)) owner.Conditions.Add(condition);
            }
        }

        return autoposts;
    }

    public static async Task<Autopost?> GetAsync(string autopostId)
    {
        return (await GetAllAsync()).FirstOrDefault(a => a.AutopostId == autopostId);
    }

    private static Autopost Read(NpgsqlDataReader reader)
    {
        return new Autopost
        {
            AutopostId = reader.GetString(0),
            Name = reader.IsDBNull(1) ? "" : reader.GetString(1),
            Enabled = !reader.IsDBNull(2) && reader.GetBoolean(2),
            ChannelId = reader.IsDBNull(3) ? 0 : (ulong)reader.GetInt64(3),
            TriggerType = ParseTrigger(reader.IsDBNull(4) ? "" : reader.GetString(4)),
            Threshold = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
            CountScopeIds = reader.IsDBNull(6) ? [] : reader.GetFieldValue<long[]>(6),
            SubtractLeaves = reader.IsDBNull(7) || reader.GetBoolean(7),
            IntervalMinutes = reader.IsDBNull(8) ? 0 : reader.GetInt32(8),
            ParentAutopostId = reader.IsDBNull(9) ? "" : reader.GetString(9),
            ParentDelaySeconds = reader.IsDBNull(10) ? 0 : reader.GetInt32(10),
            DeletePrevious = !reader.IsDBNull(11) && reader.GetBoolean(11),
            RotationMode = reader.IsDBNull(12) || reader.GetString(12) != "random"
                ? AutopostRotationMode.Sequential
                : AutopostRotationMode.Random,
            Steps = reader.IsDBNull(13) ? [] : DeserializeSteps(reader.GetString(13)),
            CounterSince = reader.IsDBNull(14) ? 0 : reader.GetInt64(14),
            JoinCount = reader.IsDBNull(15) ? 0 : reader.GetInt32(15),
            LastPostedAt = reader.IsDBNull(16) ? 0 : reader.GetInt64(16),
            LastMessageIds = reader.IsDBNull(17) ? [] : reader.GetFieldValue<long[]>(17),
            RotationState = reader.IsDBNull(18) ? [] : reader.GetFieldValue<int[]>(18),
            CreatedAt = reader.IsDBNull(19) ? 0 : reader.GetInt64(19),
            UpdatedAt = reader.IsDBNull(20) ? 0 : reader.GetInt64(20)
        };
    }

    private static AutopostCondition ReadCondition(NpgsqlDataReader reader)
    {
        AutopostConditionParams parameters;
        try
        {
            parameters = reader.IsDBNull(4)
                ? new AutopostConditionParams()
                : JsonSerializer.Deserialize<AutopostConditionParams>(reader.GetString(4), JsonOptions) ??
                  new AutopostConditionParams();
        }
        catch (JsonException)
        {
            parameters = new AutopostConditionParams();
        }

        return new AutopostCondition
        {
            ConditionId = reader.GetString(0),
            AutopostId = reader.IsDBNull(1) ? "" : reader.GetString(1),
            Type = Enum.TryParse<AutopostConditionType>(reader.IsDBNull(2) ? "" : reader.GetString(2), true,
                out var type)
                ? type
                : AutopostConditionType.Cooldown,
            Negate = !reader.IsDBNull(3) && reader.GetBoolean(3),
            Params = parameters,
            CreatedAt = reader.IsDBNull(5) ? 0 : reader.GetInt64(5)
        };
    }

    private static List<AutopostStep> DeserializeSteps(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<AutopostStep>>(json, JsonOptions) ?? [];
        }
        catch (JsonException e)
        {
            CurrentApplication.Logger.Warning(e, "Autoposts: steps JSON could not be read");
            return [];
        }
    }

    public static async Task InsertAsync(Autopost autopost)
    {
        await using var cmd = Db.CreateCommand(
            $"INSERT INTO autoposts ({Columns}) VALUES (@id, @name, @enabled, @channel, @trigger, @threshold, @scope, " +
            "@subtract, @interval, @parent, @parentDelay, @deletePrevious, @rotation, @steps, @counterSince, 0, 0, '{}', '{}', " +
            "@createdAt, @updatedAt)");
        AddConfigParameters(cmd, autopost);
        cmd.Parameters.AddWithValue("counterSince", autopost.CounterSince);
        cmd.Parameters.AddWithValue("createdAt", autopost.CreatedAt);
        await cmd.ExecuteNonQueryAsync();

        await ReplaceConditionsAsync(autopost);
    }

    /// <summary>Writes the configuration and the conditions. Counters and message ids stay untouched.</summary>
    public static async Task UpdateAsync(Autopost autopost)
    {
        await using (var cmd = Db.CreateCommand(
                         "UPDATE autoposts SET name = @name, enabled = @enabled, channel_id = @channel, trigger_type = @trigger, " +
                         "threshold = @threshold, count_scope_ids = @scope, subtract_leaves = @subtract, interval_minutes = @interval, " +
                         "parent_autopost_id = @parent, parent_delay_seconds = @parentDelay, delete_previous = @deletePrevious, " +
                         "rotation_mode = @rotation, steps = @steps, updated_at = @updatedAt, " +
                         "counter_since = CASE WHEN counter_since = 0 OR (NOT enabled AND @enabled) THEN @now ELSE counter_since END, " +
                         "join_count = CASE WHEN NOT enabled AND @enabled THEN 0 ELSE join_count END " +
                         "WHERE autopost_id = @id"))
        {
            autopost.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            AddConfigParameters(cmd, autopost);
            cmd.Parameters.AddWithValue("now", autopost.UpdatedAt);
            await cmd.ExecuteNonQueryAsync();
        }

        await ReplaceConditionsAsync(autopost);
    }

    private static void AddConfigParameters(NpgsqlCommand cmd, Autopost autopost)
    {
        cmd.Parameters.AddWithValue("id", autopost.AutopostId);
        cmd.Parameters.AddWithValue("name", autopost.Name);
        cmd.Parameters.AddWithValue("enabled", autopost.Enabled);
        cmd.Parameters.AddWithValue("channel", (long)autopost.ChannelId);
        cmd.Parameters.AddWithValue("trigger", TriggerKey(autopost.TriggerType));
        cmd.Parameters.AddWithValue("threshold", autopost.Threshold);
        cmd.Parameters.AddWithValue("scope", autopost.CountScopeIds);
        cmd.Parameters.AddWithValue("subtract", autopost.SubtractLeaves);
        cmd.Parameters.AddWithValue("interval", autopost.IntervalMinutes);
        cmd.Parameters.AddWithValue("parent", autopost.ParentAutopostId);
        cmd.Parameters.AddWithValue("parentDelay", autopost.ParentDelaySeconds);
        cmd.Parameters.AddWithValue("deletePrevious", autopost.DeletePrevious);
        cmd.Parameters.AddWithValue("rotation",
            autopost.RotationMode == AutopostRotationMode.Random ? "random" : "sequential");
        cmd.Parameters.AddWithValue("steps", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(autopost.Steps, JsonOptions));
        cmd.Parameters.AddWithValue("updatedAt", autopost.UpdatedAt);
    }

    private static async Task ReplaceConditionsAsync(Autopost autopost)
    {
        await using (var delete = Db.CreateCommand("DELETE FROM autopost_conditions WHERE autopost_id = @id"))
        {
            delete.Parameters.AddWithValue("id", autopost.AutopostId);
            await delete.ExecuteNonQueryAsync();
        }

        foreach (var condition in autopost.Conditions)
        {
            if (string.IsNullOrEmpty(condition.ConditionId)) condition.ConditionId = ToolSet.GenerateCaseID();
            if (condition.CreatedAt == 0) condition.CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            condition.AutopostId = autopost.AutopostId;

            await using var cmd = Db.CreateCommand(
                "INSERT INTO autopost_conditions (condition_id, autopost_id, condition_type, negate, params, created_at) " +
                "VALUES (@id, @autopost, @type, @negate, @params, @createdAt)");
            cmd.Parameters.AddWithValue("id", condition.ConditionId);
            cmd.Parameters.AddWithValue("autopost", autopost.AutopostId);
            cmd.Parameters.AddWithValue("type", condition.Type.ToString());
            cmd.Parameters.AddWithValue("negate", condition.Negate);
            cmd.Parameters.AddWithValue("params", NpgsqlDbType.Jsonb,
                JsonSerializer.Serialize(condition.Params, JsonOptions));
            cmd.Parameters.AddWithValue("createdAt", condition.CreatedAt);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public static async Task DeleteAsync(string autopostId)
    {
        foreach (var sql in new[]
                 {
                     "DELETE FROM autopost_conditions WHERE autopost_id = @id",
                     "DELETE FROM autopost_queue WHERE autopost_id = @id",
                     "UPDATE autoposts SET parent_autopost_id = '' WHERE parent_autopost_id = @id",
                     "DELETE FROM autoposts WHERE autopost_id = @id"
                 })
        {
            await using var cmd = Db.CreateCommand(sql);
            cmd.Parameters.AddWithValue("id", autopostId);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public static async Task<List<AutopostQueueEntry>> GetQueueAsync(string? autopostId = null)
    {
        var entries = new List<AutopostQueueEntry>();
        await using var cmd = Db.CreateCommand(
            "SELECT queue_id, autopost_id, step_index, due_at, check_filters FROM autopost_queue " +
            (autopostId == null ? "" : "WHERE autopost_id = @id ") + "ORDER BY due_at");
        if (autopostId != null) cmd.Parameters.AddWithValue("id", autopostId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            entries.Add(new AutopostQueueEntry
            {
                QueueId = reader.GetString(0),
                AutopostId = reader.IsDBNull(1) ? "" : reader.GetString(1),
                StepIndex = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                DueAt = reader.IsDBNull(3) ? 0 : reader.GetInt64(3),
                CheckFilters = !reader.IsDBNull(4) && reader.GetBoolean(4)
            });

        return entries;
    }

    private static async Task EnqueueAsync(string autopostId, int stepIndex, long dueAt, bool checkFilters)
    {
        await using var cmd = Db.CreateCommand(
            "INSERT INTO autopost_queue (queue_id, autopost_id, step_index, due_at, check_filters) " +
            "VALUES (@queueId, @id, @step, @due, @check)");
        cmd.Parameters.AddWithValue("queueId", ToolSet.GenerateCaseID());
        cmd.Parameters.AddWithValue("id", autopostId);
        cmd.Parameters.AddWithValue("step", stepIndex);
        cmd.Parameters.AddWithValue("due", dueAt);
        cmd.Parameters.AddWithValue("check", checkFilters);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task AdjustJoinCountAsync(int delta)
    {
        var sql = delta > 0
            ? "UPDATE autoposts SET join_count = join_count + 1 WHERE enabled AND trigger_type = 'joins'"
            : "UPDATE autoposts SET join_count = GREATEST(join_count - 1, 0) WHERE enabled AND trigger_type = 'joins' AND subtract_leaves";
        await using var cmd = Db.CreateCommand(sql);
        await cmd.ExecuteNonQueryAsync();
    }

    public static string TriggerKey(AutopostTriggerType type)
    {
        return type switch
        {
            AutopostTriggerType.Joins => "joins",
            AutopostTriggerType.Interval => "interval",
            AutopostTriggerType.AfterAutopost => "after_autopost",
            _ => "messages"
        };
    }

    private static AutopostTriggerType ParseTrigger(string raw)
    {
        return raw switch
        {
            "joins" => AutopostTriggerType.Joins,
            "interval" => AutopostTriggerType.Interval,
            "after_autopost" => AutopostTriggerType.AfterAutopost,
            _ => AutopostTriggerType.Messages
        };
    }

    #endregion

    #region Counting

    public static async Task<long> CountMessagesAsync(IEnumerable<ulong> channelIds, long sinceUnix)
    {
        var ids = channelIds.Select(id => (long)id).ToArray();
        if (ids.Length == 0) return 0;

        await using var cmd = Db.CreateCommand(
            "SELECT COUNT(*) FROM metrics_messages WHERE channelid = ANY(@ids) AND timestamp > @since");
        cmd.Parameters.AddWithValue("ids", ids);
        cmd.Parameters.AddWithValue("since", sinceUnix);
        return await cmd.ExecuteScalarAsync() is long count ? count : 0;
    }

    private static IEnumerable<ulong> CountScope(Autopost autopost)
    {
        return autopost.CountScopeIds.Length == 0
            ? [autopost.ChannelId]
            : MetricsQueryService.ExpandScope(CurrentApplication.TargetGuild, autopost.CountScopeIds);
    }

    public static async Task<Progress> GetProgressAsync(Autopost autopost, IReadOnlyCollection<Autopost> all)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        switch (autopost.TriggerType)
        {
            case AutopostTriggerType.Messages:
            {
                var count = autopost.CounterSince > 0
                    ? await CountMessagesAsync(CountScope(autopost), autopost.CounterSince)
                    : 0;
                return new Progress(count, autopost.Threshold, $"{count:N0} / {autopost.Threshold:N0} Nachrichten");
            }
            case AutopostTriggerType.Joins:
                return new Progress(autopost.JoinCount, autopost.Threshold,
                    $"{autopost.JoinCount:N0} / {autopost.Threshold:N0} Joins");
            case AutopostTriggerType.Interval:
            {
                var total = autopost.IntervalMinutes * 60L;
                var elapsed = autopost.LastPostedAt == 0 ? total : Math.Min(now - autopost.LastPostedAt, total);
                var remaining = (total - elapsed + 59) / 60;
                return new Progress(elapsed, total, remaining <= 0 ? "fällig" : $"noch {remaining} Min");
            }
            default:
            {
                var parent = all.FirstOrDefault(a => a.AutopostId == autopost.ParentAutopostId);
                var delay = FormatDuration(autopost.ParentDelaySeconds);
                return new Progress(0, 0, parent == null
                    ? "Kein Auslöser gewählt"
                    : $"{delay} nach \"{parent.Name}\"");
            }
        }
    }

    public static string FormatDuration(int seconds)
    {
        if (seconds <= 0) return "direkt";
        if (seconds % 3600 == 0) return $"{seconds / 3600} Std";
        if (seconds % 60 == 0) return $"{seconds / 60} Min";
        return $"{seconds} Sek";
    }

    #endregion

    #region Evaluation

    public static async Task EvaluateAllAsync()
    {
        if (CurrentApplication.TargetGuild is null) return;

        await Gate.WaitAsync();
        try
        {
            var all = await GetAllAsync();
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            foreach (var autopost in all)
            {
                if (!autopost.Enabled || autopost.TriggerType == AutopostTriggerType.AfterAutopost) continue;

                try
                {
                    if (!await TriggerReachedAsync(autopost, now)) continue;
                    if (!(await AutopostConditionEvaluator.EvaluateAsync(autopost)).Passed) continue;

                    await FireAsync(autopost, all, now, false);
                }
                catch (Exception e)
                {
                    CurrentApplication.Logger.Error(e, "Autoposts: evaluation failed for {AutopostId}",
                        autopost.AutopostId);
                }
            }

            await ProcessQueueAsync(all, now);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<bool> TriggerReachedAsync(Autopost autopost, long now)
    {
        switch (autopost.TriggerType)
        {
            case AutopostTriggerType.Messages:
                if (autopost.Threshold <= 0) return false;
                if (autopost.CounterSince <= 0)
                {
                    await using var cmd = Db.CreateCommand(
                        "UPDATE autoposts SET counter_since = @now WHERE autopost_id = @id");
                    cmd.Parameters.AddWithValue("now", now);
                    cmd.Parameters.AddWithValue("id", autopost.AutopostId);
                    await cmd.ExecuteNonQueryAsync();
                    return false;
                }

                return await CountMessagesAsync(CountScope(autopost), autopost.CounterSince) >= autopost.Threshold;
            case AutopostTriggerType.Joins:
                return autopost.Threshold > 0 && autopost.JoinCount >= autopost.Threshold;
            case AutopostTriggerType.Interval:
                return autopost.IntervalMinutes > 0 && now - autopost.LastPostedAt >= autopost.IntervalMinutes * 60L;
            default:
                return false;
        }
    }

    private static async Task ProcessQueueAsync(List<Autopost> all, long now)
    {
        foreach (var entry in await GetQueueAsync())
        {
            if (entry.DueAt > now) break;

            await using (var cmd = Db.CreateCommand("DELETE FROM autopost_queue WHERE queue_id = @id"))
            {
                cmd.Parameters.AddWithValue("id", entry.QueueId);
                await cmd.ExecuteNonQueryAsync();
            }

            var autopost = all.FirstOrDefault(a => a.AutopostId == entry.AutopostId);
            if (autopost == null) continue;

            try
            {
                if (entry.StepIndex > 0)
                {
                    await SendStepAsync(autopost, entry.StepIndex);
                    continue;
                }

                if (entry.CheckFilters &&
                    (!autopost.Enabled || !(await AutopostConditionEvaluator.EvaluateAsync(autopost)).Passed))
                    continue;

                await FireAsync(autopost, all, now, !entry.CheckFilters);
            }
            catch (Exception e)
            {
                CurrentApplication.Logger.Error(e, "Autoposts: queued send failed for {AutopostId}",
                    autopost.AutopostId);
            }
        }
    }

    /// <summary>
    ///     Sends the first step right away and queues the rest, then queues follow-up autoposts behind the
    ///     last step. A manual run pulls disabled follow-ups along and skips their filters.
    /// </summary>
    private static async Task<SendResult> FireAsync(Autopost autopost, List<Autopost> all, long now, bool manual)
    {
        var result = await SendStepAsync(autopost, 0);

        await using (var cmd = Db.CreateCommand(
                         "UPDATE autoposts SET counter_since = @now, join_count = 0, last_posted_at = @now WHERE autopost_id = @id"))
        {
            cmd.Parameters.AddWithValue("now", now);
            cmd.Parameters.AddWithValue("id", autopost.AutopostId);
            await cmd.ExecuteNonQueryAsync();
        }

        autopost.CounterSince = now;
        autopost.JoinCount = 0;
        autopost.LastPostedAt = now;

        if (!result.Sent) return result;

        long offset = 0;
        for (var i = 1; i < autopost.Steps.Count; i++)
        {
            offset += Math.Max(autopost.Steps[i].DelaySeconds, 0);
            await EnqueueAsync(autopost.AutopostId, i, now + offset, false);
        }

        var queued = (await GetQueueAsync()).Where(q => q.StepIndex == 0).Select(q => q.AutopostId).ToHashSet();
        foreach (var child in all.Where(a => a.TriggerType == AutopostTriggerType.AfterAutopost &&
                                             a.ParentAutopostId == autopost.AutopostId &&
                                             a.AutopostId != autopost.AutopostId))
        {
            if (!manual && !child.Enabled) continue;
            if (queued.Contains(child.AutopostId)) continue;

            await EnqueueAsync(child.AutopostId, 0, now + offset + Math.Max(child.ParentDelaySeconds, 0), !manual);
        }

        return result;
    }

    public static async Task<SendResult> SendNowAsync(string autopostId)
    {
        await Gate.WaitAsync();
        try
        {
            var all = await GetAllAsync();
            var autopost = all.FirstOrDefault(a => a.AutopostId == autopostId);
            if (autopost == null) return new SendResult(false, "Diesen Autopost gibt es nicht mehr.");

            return await FireAsync(autopost, all, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), true);
        }
        finally
        {
            Gate.Release();
        }
    }

    #endregion

    #region Sending

    private static async Task<SendResult> SendStepAsync(Autopost autopost, int stepIndex)
    {
        var guild = CurrentApplication.TargetGuild;
        if (guild == null) return new SendResult(false, "Der Bot ist gerade nicht mit dem Server verbunden.");

        if (!guild.Channels.TryGetValue(autopost.ChannelId, out var channel))
            return new SendResult(false, "Der Zielkanal existiert nicht oder ist nicht gesetzt.");

        if (stepIndex >= autopost.Steps.Count) return new SendResult(false, "Dieser Schritt existiert nicht mehr.");

        var variants = autopost.Steps[stepIndex].Variants.Where(v => !IsEmpty(v)).ToList();
        if (variants.Count == 0) return new SendResult(false, "Dieser Schritt hat keinen Inhalt.");

        var rotation = autopost.RotationState.ToList();
        while (rotation.Count <= stepIndex) rotation.Add(0);

        int pick;
        if (autopost.RotationMode == AutopostRotationMode.Random)
        {
            pick = Random.Shared.Next(variants.Count);
        }
        else
        {
            pick = Math.Abs(rotation[stepIndex]) % variants.Count;
            rotation[stepIndex] = (pick + 1) % variants.Count;
        }

        var messageIds = autopost.LastMessageIds.ToList();
        if (stepIndex == 0)
        {
            if (autopost.DeletePrevious) await DeleteMessagesAsync(channel, autopost.LastMessageIds);
            messageIds.Clear();
        }

        DiscordMessage message;
        try
        {
            message = await channel.SendMessageAsync(BuildMessage(variants[pick]));
        }
        catch (Exception e)
        {
            CurrentApplication.Logger.Error(e, "Autoposts: sending {AutopostId} step {Step} failed",
                autopost.AutopostId, stepIndex);
            return new SendResult(false, "Discord hat die Nachricht abgelehnt. Details stehen im Log.");
        }

        messageIds.Add((long)message.Id);
        autopost.LastMessageIds = messageIds.ToArray();
        autopost.RotationState = rotation.ToArray();

        await using var cmd = Db.CreateCommand(
            "UPDATE autoposts SET last_message_ids = @messages, rotation_state = @rotation WHERE autopost_id = @id");
        cmd.Parameters.AddWithValue("messages", autopost.LastMessageIds);
        cmd.Parameters.AddWithValue("rotation", autopost.RotationState);
        cmd.Parameters.AddWithValue("id", autopost.AutopostId);
        await cmd.ExecuteNonQueryAsync();

        return new SendResult(true, "Gesendet.");
    }

    private static async Task DeleteMessagesAsync(DiscordChannel channel, IEnumerable<long> messageIds)
    {
        foreach (var id in messageIds)
            try
            {
                var message = await channel.GetMessageAsync((ulong)id);
                await message.DeleteAsync();
            }
            catch (Exception e)
            {
                CurrentApplication.Logger.Warning(e, "Autoposts: could not delete previous message {MessageId}", id);
            }
    }

    public static bool IsEmpty(AutopostVariant variant)
    {
        return string.IsNullOrWhiteSpace(variant.Content) && variant.Embed.IsEmpty;
    }

    public static DiscordMessageBuilder BuildMessage(AutopostVariant variant)
    {
        var builder = new DiscordMessageBuilder();

        var content = InfoPanelTemplateResolver.Resolve(variant.Content);
        if (!string.IsNullOrWhiteSpace(content)) builder.WithContent(Truncate(content, 2000));

        if (!variant.Embed.IsEmpty) builder.AddEmbed(BuildEmbed(variant.Embed));

        var buttons = variant.Buttons
            .Select(b => (Button: b, Url: InfoPanelTemplateResolver.Resolve(b.Url).Trim()))
            .Where(b => IsHttpUrl(b.Url) &&
                        (!string.IsNullOrWhiteSpace(b.Button.Label) || !string.IsNullOrWhiteSpace(b.Button.Emoji)))
            .Select(b => (DiscordComponent)new DiscordLinkButtonComponent(b.Url,
                string.IsNullOrWhiteSpace(b.Button.Label) ? null : Truncate(b.Button.Label, DiscordLimits.ButtonLabel),
                false, ComponentEmojiParser.Parse(b.Button.Emoji)))
            .Take(DiscordLimits.ButtonsPerRow * DiscordLimits.ActionRowsPerMessage)
            .ToList();

        foreach (var row in buttons.Chunk(DiscordLimits.ButtonsPerRow)) builder.AddComponents(row);

        return builder;
    }

    private static DiscordEmbed BuildEmbed(AutopostEmbed embed)
    {
        var builder = new DiscordEmbedBuilder();

        var authorName = InfoPanelTemplateResolver.Resolve(embed.AuthorName);
        if (!string.IsNullOrWhiteSpace(authorName))
        {
            var icon = InfoPanelTemplateResolver.Resolve(embed.AuthorIconUrl).Trim();
            builder.WithAuthor(Truncate(authorName, DiscordLimits.EmbedAuthorName), null,
                IsHttpUrl(icon) ? icon : null);
        }

        var title = InfoPanelTemplateResolver.Resolve(embed.Title);
        if (!string.IsNullOrWhiteSpace(title)) builder.WithTitle(Truncate(title, DiscordLimits.EmbedTitle));

        var description = InfoPanelTemplateResolver.Resolve(embed.Description);
        if (!string.IsNullOrWhiteSpace(description))
            builder.WithDescription(Truncate(description, DiscordLimits.EmbedDescription));

        var color = ParseColor(embed.Color);
        if (color.HasValue) builder.WithColor(color.Value);

        var thumbnail = InfoPanelTemplateResolver.Resolve(embed.ThumbnailUrl).Trim();
        if (IsHttpUrl(thumbnail)) builder.WithThumbnail(thumbnail);

        var image = InfoPanelTemplateResolver.Resolve(embed.ImageUrl).Trim();
        if (IsHttpUrl(image)) builder.WithImageUrl(image);

        var footer = InfoPanelTemplateResolver.Resolve(embed.Footer);
        if (!string.IsNullOrWhiteSpace(footer)) builder.WithFooter(Truncate(footer, DiscordLimits.EmbedFooter));

        return builder.Build();
    }

    public static DiscordColor? ParseColor(string? hex)
    {
        var value = hex?.Trim().TrimStart('#') ?? "";
        if (value.Length != 6) return null;

        return int.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out var rgb)
            ? new DiscordColor(rgb)
            : null;
    }

    private static bool IsHttpUrl(string? url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static string Truncate(string value, int max)
    {
        return value.Length <= max ? value : value[..max];
    }

    #endregion
}
