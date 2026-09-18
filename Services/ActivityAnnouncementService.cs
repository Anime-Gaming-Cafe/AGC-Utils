#region

using System.Text.RegularExpressions;
using AGC_Management.Entities.ActivityRoles;

#endregion

namespace AGC_Management.Services;

/// <summary>
///     Bundles two or more <see cref="ActivityRoleService" /> rules into one shared announcement message
///     (e.g. a chat leaderboard and a voice leaderboard posted together). Purely a reporting layer on top
///     of the existing per-rule ranking/threshold computation - it never grants or revokes roles itself,
///     that stays entirely with each rule's own evaluation.
/// </summary>
public static class ActivityAnnouncementService
{
    private static readonly Regex AliasRankPlaceholder = new(@"\{([A-Za-z0-9_-]+)\.rank(\d+)\}",
        RegexOptions.Compiled);

    private static readonly Regex AliasWinnersPlaceholder = new(@"\{([A-Za-z0-9_-]+)\.winners\}",
        RegexOptions.Compiled);

    #region Groups

    public static async Task<List<ActivityAnnouncementGroup>> GetGroupsAsync()
    {
        var groups = new List<ActivityAnnouncementGroup>();
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using (var cmd = con.CreateCommand(
                         "SELECT group_id, name, channel_id, interval_days, message, last_announced_at, created_by, created_at " +
                         "FROM activity_announcement_groups ORDER BY name"))
        {
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) groups.Add(ReadGroup(reader));
        }

        if (groups.Count > 0)
        {
            var byId = groups.ToDictionary(g => g.GroupId);
            foreach (var link in await GetAllGroupRulesAsync())
                if (byId.TryGetValue(link.GroupId, out var group))
                    group.Rules.Add(link);
        }

        return groups;
    }

    public static async Task<ActivityAnnouncementGroup?> GetGroupAsync(string groupId)
    {
        var groups = await GetGroupsAsync();
        return groups.FirstOrDefault(g => g.GroupId == groupId);
    }

    private static ActivityAnnouncementGroup ReadGroup(NpgsqlDataReader reader)
    {
        return new ActivityAnnouncementGroup
        {
            GroupId = reader.GetString(0),
            Name = reader.IsDBNull(1) ? "" : reader.GetString(1),
            ChannelId = reader.IsDBNull(2) ? 0 : (ulong)reader.GetInt64(2),
            IntervalDays = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
            Message = reader.IsDBNull(4) ? "" : reader.GetString(4),
            LastAnnouncedAt = reader.IsDBNull(5) ? 0 : reader.GetInt64(5),
            CreatedBy = reader.IsDBNull(6) ? 0 : (ulong)reader.GetInt64(6),
            CreatedAt = reader.IsDBNull(7) ? 0 : reader.GetInt64(7)
        };
    }

    public static async Task AddGroupAsync(ActivityAnnouncementGroup group)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = con.CreateCommand(
            "INSERT INTO activity_announcement_groups (group_id, name, channel_id, interval_days, message, created_by, created_at) " +
            "VALUES (@group_id, @name, @channel_id, @interval_days, @message, @created_by, @created_at)");
        cmd.Parameters.AddWithValue("group_id", group.GroupId);
        cmd.Parameters.AddWithValue("name", group.Name);
        cmd.Parameters.AddWithValue("channel_id", (long)group.ChannelId);
        cmd.Parameters.AddWithValue("interval_days", group.IntervalDays);
        cmd.Parameters.AddWithValue("message", group.Message);
        cmd.Parameters.AddWithValue("created_by", (long)group.CreatedBy);
        cmd.Parameters.AddWithValue("created_at", group.CreatedAt);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task UpdateGroupAsync(ActivityAnnouncementGroup group)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = con.CreateCommand(
            "UPDATE activity_announcement_groups SET name = @name, channel_id = @channel_id, " +
            "interval_days = @interval_days, message = @message WHERE group_id = @group_id");
        cmd.Parameters.AddWithValue("group_id", group.GroupId);
        cmd.Parameters.AddWithValue("name", group.Name);
        cmd.Parameters.AddWithValue("channel_id", (long)group.ChannelId);
        cmd.Parameters.AddWithValue("interval_days", group.IntervalDays);
        cmd.Parameters.AddWithValue("message", group.Message);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task DeleteGroupAsync(string groupId)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using (var cmdLinks =
                     con.CreateCommand("DELETE FROM activity_announcement_group_rules WHERE group_id = @group_id"))
        {
            cmdLinks.Parameters.AddWithValue("group_id", groupId);
            await cmdLinks.ExecuteNonQueryAsync();
        }

        await using var cmd = con.CreateCommand("DELETE FROM activity_announcement_groups WHERE group_id = @group_id");
        cmd.Parameters.AddWithValue("group_id", groupId);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task SetLastAnnouncedAsync(string groupId, long timestamp)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = con.CreateCommand(
            "UPDATE activity_announcement_groups SET last_announced_at = @timestamp WHERE group_id = @group_id");
        cmd.Parameters.AddWithValue("group_id", groupId);
        cmd.Parameters.AddWithValue("timestamp", timestamp);
        await cmd.ExecuteNonQueryAsync();
    }

    #endregion

    #region Rule links

    private static async Task<List<ActivityAnnouncementGroupRule>> GetAllGroupRulesAsync()
    {
        var links = new List<ActivityAnnouncementGroupRule>();
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = con.CreateCommand(
            "SELECT group_id, rule_id, alias FROM activity_announcement_group_rules ORDER BY group_id, alias");
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            links.Add(new ActivityAnnouncementGroupRule
            {
                GroupId = reader.GetString(0),
                RuleId = reader.GetString(1),
                Alias = reader.IsDBNull(2) ? "" : reader.GetString(2)
            });

        return links;
    }

    /// <summary>False means the alias or the rule is already used in this group (unique index / primary key).</summary>
    public static async Task<bool> AddRuleLinkAsync(string groupId, string ruleId, string alias)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        try
        {
            await using var cmd = con.CreateCommand(
                "INSERT INTO activity_announcement_group_rules (group_id, rule_id, alias) VALUES (@group_id, @rule_id, @alias)");
            cmd.Parameters.AddWithValue("group_id", groupId);
            cmd.Parameters.AddWithValue("rule_id", ruleId);
            cmd.Parameters.AddWithValue("alias", alias);
            await cmd.ExecuteNonQueryAsync();
            return true;
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return false;
        }
    }

    public static async Task RemoveRuleLinkAsync(string groupId, string ruleId)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = con.CreateCommand(
            "DELETE FROM activity_announcement_group_rules WHERE group_id = @group_id AND rule_id = @rule_id");
        cmd.Parameters.AddWithValue("group_id", groupId);
        cmd.Parameters.AddWithValue("rule_id", ruleId);
        await cmd.ExecuteNonQueryAsync();
    }

    #endregion

    #region Evaluation & rendering

    private static async Task<Dictionary<string, (ActivityRoleRule Rule, List<ActivityRoleService.RankedGrant> Winners)>>
        ComputeAliasDataAsync(ActivityAnnouncementGroup group, DiscordGuild guild)
    {
        var data = new Dictionary<string, (ActivityRoleRule, List<ActivityRoleService.RankedGrant>)>();
        foreach (var link in group.Rules)
        {
            if (string.IsNullOrEmpty(link.Alias)) continue;

            var rule = await ActivityRoleService.GetRuleAsync(link.RuleId);
            if (rule == null) continue;

            var winners = await ActivityRoleService.ComputeDesiredGrantsAsync(rule, guild);
            data[link.Alias] = (rule, winners);
        }

        return data;
    }

    private static string BuildMessage(string template,
        Dictionary<string, (ActivityRoleRule Rule, List<ActivityRoleService.RankedGrant> Winners)> aliasData)
    {
        var message = AliasRankPlaceholder.Replace(template, m =>
        {
            if (!aliasData.TryGetValue(m.Groups[1].Value, out var entry)) return "";

            var rank = int.Parse(m.Groups[2].Value);
            var winner = entry.Winners.FirstOrDefault(w => w.Rank == rank);
            return winner.Rank == rank ? ActivityRoleService.FormatWinnerLine(winner, entry.Rule) : "";
        });

        return AliasWinnersPlaceholder.Replace(message, m =>
        {
            if (!aliasData.TryGetValue(m.Groups[1].Value, out var entry)) return "";

            var joined = entry.Winners
                .OrderBy(w => w.Rank == 0 ? int.MaxValue : w.Rank)
                .Select(w => ActivityRoleService.FormatWinnerLine(w, entry.Rule));
            return string.Join("\n", joined);
        });
    }

    /// <summary>Renders the group's saved template against live data - for the WebUI's own preview button.</summary>
    public static async Task<string> PreviewMessageAsync(string groupId, string template)
    {
        var group = await GetGroupAsync(groupId);
        var guild = CurrentApplication.TargetGuild;
        if (group == null || guild == null) return "";

        var aliasData = await ComputeAliasDataAsync(group, guild);
        return BuildMessage(template, aliasData);
    }

    private static async Task<bool> SendGroupAnnouncementAsync(ActivityAnnouncementGroup group, DiscordGuild guild)
    {
        if (group.ChannelId == 0) return false;
        if (group.Rules.Count == 0) return false;
        if (!guild.Channels.TryGetValue(group.ChannelId, out var channel)) return false;

        var aliasData = await ComputeAliasDataAsync(group, guild);
        if (aliasData.Count == 0 || aliasData.Values.All(d => d.Winners.Count == 0)) return false;

        var message = BuildMessage(group.Message, aliasData);
        if (string.IsNullOrWhiteSpace(message)) return false;

        try
        {
            await channel.SendMessageAsync(message);
            return true;
        }
        catch (Exception e)
        {
            CurrentApplication.Logger.Error(e,
                "ActivityAnnouncementGroups: failed to send announcement for group {GroupId}", group.GroupId);
            return false;
        }
    }

    public static async Task EvaluateGroupAsync(ActivityAnnouncementGroup group)
    {
        if (group.ChannelId == 0 || group.IntervalDays <= 0) return;

        var guild = CurrentApplication.TargetGuild;
        if (guild == null) return;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (now - group.LastAnnouncedAt < group.IntervalDays * 86400L) return;

        if (await SendGroupAnnouncementAsync(group, guild)) await SetLastAnnouncedAsync(group.GroupId, now);
    }

    /// <summary>Manual "send now" for the WebUI, mirrors <see cref="ActivityRoleService.TriggerAnnouncementAsync" />.</summary>
    public static async Task<bool> TriggerAnnouncementAsync(string groupId)
    {
        var group = await GetGroupAsync(groupId);
        var guild = CurrentApplication.TargetGuild;
        if (group == null || guild == null) return false;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (!await SendGroupAnnouncementAsync(group, guild)) return false;

        await SetLastAnnouncedAsync(groupId, now);
        return true;
    }

    #endregion
}
