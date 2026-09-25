#region

using AGC_Management.Entities.Metrics;
using AGC_Management.Entities.Selfroles;
using AGC_Management.Utils;

#endregion

namespace AGC_Management.Services;

/// <summary>
///     Reads and writes the selfrole panel, and applies the role changes members ask for. The whole set
///     is hit on every interaction, so it is cached for a short while the way
///     <see cref="InfoPanelService" /> does it.
/// </summary>
public static class SelfroleService
{
    public const string Section = "Selfroles";

    public const string AuditReason = "Selfroles";

    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
    private static readonly SemaphoreSlim LoadLock = new(1, 1);

    private static SelfrolePanel? _cache;
    private static DateTime _cacheExpires = DateTime.MinValue;

    private static NpgsqlDataSource Db =>
        CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

    public static void Invalidate()
    {
        _cacheExpires = DateTime.MinValue;
    }

    #region Loading

    public static async Task<SelfrolePanel> GetPanelAsync()
    {
        if (_cache is not null && _cacheExpires > DateTime.UtcNow) return _cache;

        await LoadLock.WaitAsync();
        try
        {
            if (_cache is not null && _cacheExpires > DateTime.UtcNow) return _cache;

            var panel = await LoadPanelAsync() ?? new SelfrolePanel();
            panel.Categories = await LoadCategoriesAsync();

            var options = await LoadOptionsAsync();
            foreach (var category in panel.Categories)
                if (options.TryGetValue(category.Id, out var own))
                    category.Options = own;

            _cache = panel;
            _cacheExpires = DateTime.UtcNow + CacheTtl;
            return panel;
        }
        finally
        {
            LoadLock.Release();
        }
    }

    public static async Task<SelfroleCategory?> GetCategoryAsync(string? categoryId)
    {
        if (string.IsNullOrWhiteSpace(categoryId)) return null;
        var panel = await GetPanelAsync();
        return panel.Categories.FirstOrDefault(c =>
            string.Equals(c.Id, categoryId, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<SelfrolePanel?> LoadPanelAsync()
    {
        await using var cmd = Db.CreateCommand(
            "SELECT id, channel_id, message_id, enabled, header_title, header_text, author_name, " +
            "author_icon_mode, author_icon_url, banner_mode, banner_url, color, auto_repost, rendered_hash " +
            "FROM selfrole_panel WHERE id = @id");
        cmd.Parameters.AddWithValue("id", SelfrolePanel.PanelId);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return new SelfrolePanel
        {
            Id = reader.GetString(0),
            ChannelId = reader.IsDBNull(1) ? 0 : (ulong)reader.GetInt64(1),
            MessageId = reader.IsDBNull(2) ? 0 : (ulong)reader.GetInt64(2),
            Enabled = !reader.IsDBNull(3) && reader.GetBoolean(3),
            HeaderTitle = Str(reader, 4),
            HeaderText = Str(reader, 5),
            AuthorName = Str(reader, 6),
            AuthorIconMode = Str(reader, 7, SelfrolePanel.ModeGuild),
            AuthorIconUrl = Str(reader, 8),
            BannerMode = Str(reader, 9, SelfrolePanel.ModeGuild),
            BannerUrl = Str(reader, 10),
            Color = Str(reader, 11, "2F84A2"),
            AutoRepost = reader.IsDBNull(12) || reader.GetBoolean(12),
            RenderedHash = Str(reader, 13)
        };
    }

    private static async Task<List<SelfroleCategory>> LoadCategoriesAsync()
    {
        var categories = new List<SelfroleCategory>();
        await using var cmd = Db.CreateCommand(
            "SELECT id, name, emoji, button_style, kind, placeholder, min_values, max_values, allow_clear, " +
            "sticky, auto_assign, position, enabled FROM selfrole_categories ORDER BY position, name");

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            categories.Add(new SelfroleCategory
            {
                Id = reader.GetString(0),
                Name = Str(reader, 1),
                Emoji = Str(reader, 2),
                ButtonStyle = reader.IsDBNull(3) ? (int)ButtonStyle.Secondary : reader.GetInt32(3),
                Kind = Str(reader, 4, SelfroleCategory.KindSelect),
                Placeholder = Str(reader, 5),
                MinValues = reader.IsDBNull(6) ? 0 : reader.GetInt32(6),
                MaxValues = reader.IsDBNull(7) ? 0 : reader.GetInt32(7),
                AllowClear = reader.IsDBNull(8) || reader.GetBoolean(8),
                Sticky = reader.IsDBNull(9) || reader.GetBoolean(9),
                AutoAssign = !reader.IsDBNull(10) && reader.GetBoolean(10),
                Position = reader.IsDBNull(11) ? 0 : reader.GetInt32(11),
                Enabled = reader.IsDBNull(12) || reader.GetBoolean(12)
            });

        return categories;
    }

    private static async Task<Dictionary<string, List<SelfroleOption>>> LoadOptionsAsync()
    {
        var result = new Dictionary<string, List<SelfroleOption>>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = Db.CreateCommand(
            "SELECT id, category_id, role_id, label, description, emoji, match_patterns, position, enabled " +
            "FROM selfrole_options ORDER BY category_id, position");

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var option = new SelfroleOption
            {
                Id = reader.GetString(0),
                CategoryId = reader.GetString(1),
                RoleId = reader.IsDBNull(2) ? 0 : (ulong)reader.GetInt64(2),
                Label = Str(reader, 3),
                Description = Str(reader, 4),
                Emoji = Str(reader, 5),
                MatchPatterns = reader.IsDBNull(6) ? [] : reader.GetFieldValue<string[]>(6),
                Position = reader.IsDBNull(7) ? 0 : reader.GetInt32(7),
                Enabled = reader.IsDBNull(8) || reader.GetBoolean(8)
            };

            if (!result.TryGetValue(option.CategoryId, out var list))
            {
                list = [];
                result[option.CategoryId] = list;
            }

            list.Add(option);
        }

        return result;
    }

    private static string Str(NpgsqlDataReader reader, int ordinal, string fallback = "")
    {
        if (reader.IsDBNull(ordinal)) return fallback;
        var value = reader.GetString(ordinal);
        return string.IsNullOrEmpty(value) ? fallback : value;
    }

    #endregion

    #region Writing

    /// <summary>
    ///     Writes the panel row. Update-then-insert rather than ON CONFLICT, for the same reason
    ///     <see cref="InfoPanelService.UpsertAsync" /> does it: it still works on a database whose
    ///     unique index could not be created.
    /// </summary>
    public static async Task UpsertPanelAsync(SelfrolePanel panel)
    {
        await using (var update = Db.CreateCommand(
                         "UPDATE selfrole_panel SET channel_id = @channel, message_id = @message, " +
                         "enabled = @enabled, header_title = @headerTitle, header_text = @headerText, " +
                         "author_name = @authorName, author_icon_mode = @authorIconMode, " +
                         "author_icon_url = @authorIconUrl, banner_mode = @bannerMode, banner_url = @bannerUrl, " +
                         "color = @color, auto_repost = @autoRepost, rendered_hash = @hash WHERE id = @id"))
        {
            BindPanel(update, panel);
            update.Parameters.AddWithValue("channel", (long)panel.ChannelId);
            update.Parameters.AddWithValue("message", (long)panel.MessageId);
            update.Parameters.AddWithValue("enabled", panel.Enabled);
            update.Parameters.AddWithValue("hash", panel.RenderedHash ?? "");
            if (await update.ExecuteNonQueryAsync() > 0)
            {
                Invalidate();
                return;
            }
        }

        await using var insert = Db.CreateCommand(
            "INSERT INTO selfrole_panel (id, channel_id, message_id, enabled, header_title, header_text, " +
            "author_name, author_icon_mode, author_icon_url, banner_mode, banner_url, color, auto_repost, " +
            "rendered_hash) VALUES (@id, @channel, @message, @enabled, @headerTitle, @headerText, @authorName, " +
            "@authorIconMode, @authorIconUrl, @bannerMode, @bannerUrl, @color, @autoRepost, @hash)");
        BindPanel(insert, panel);
        insert.Parameters.AddWithValue("channel", (long)panel.ChannelId);
        insert.Parameters.AddWithValue("message", (long)panel.MessageId);
        insert.Parameters.AddWithValue("enabled", panel.Enabled);
        insert.Parameters.AddWithValue("hash", panel.RenderedHash ?? "");
        await insert.ExecuteNonQueryAsync();

        Invalidate();
    }

    /// <summary>
    ///     Writes only what the settings page owns. Channel, message id and hash are left alone on purpose:
    ///     the dashboard holds a copy that is seconds old, and the refresh task may have reposted the
    ///     message in the meantime. Saving a text must not send the panel back to a message that is gone.
    /// </summary>
    public static async Task UpdatePanelSettingsAsync(SelfrolePanel panel)
    {
        await using var cmd = Db.CreateCommand(
            "UPDATE selfrole_panel SET header_title = @headerTitle, header_text = @headerText, " +
            "author_name = @authorName, author_icon_mode = @authorIconMode, author_icon_url = @authorIconUrl, " +
            "banner_mode = @bannerMode, banner_url = @bannerUrl, color = @color, auto_repost = @autoRepost " +
            "WHERE id = @id");
        BindPanel(cmd, panel);
        await cmd.ExecuteNonQueryAsync();

        Invalidate();
    }

    private static void BindPanel(NpgsqlCommand cmd, SelfrolePanel panel)
    {
        cmd.Parameters.AddWithValue("id", string.IsNullOrWhiteSpace(panel.Id) ? SelfrolePanel.PanelId : panel.Id);
        cmd.Parameters.AddWithValue("headerTitle", panel.HeaderTitle ?? "");
        cmd.Parameters.AddWithValue("headerText", panel.HeaderText ?? "");
        cmd.Parameters.AddWithValue("authorName", panel.AuthorName ?? "");
        cmd.Parameters.AddWithValue("authorIconMode", panel.AuthorIconMode ?? SelfrolePanel.ModeNone);
        cmd.Parameters.AddWithValue("authorIconUrl", panel.AuthorIconUrl ?? "");
        cmd.Parameters.AddWithValue("bannerMode", panel.BannerMode ?? SelfrolePanel.ModeNone);
        cmd.Parameters.AddWithValue("bannerUrl", panel.BannerUrl ?? "");
        cmd.Parameters.AddWithValue("color", string.IsNullOrWhiteSpace(panel.Color) ? "2F84A2" : panel.Color);
        cmd.Parameters.AddWithValue("autoRepost", panel.AutoRepost);
    }

    public static async Task SetPanelLocationAsync(ulong channelId, ulong messageId)
    {
        await using var cmd = Db.CreateCommand(
            "UPDATE selfrole_panel SET channel_id = @channel, message_id = @message WHERE id = @id");
        cmd.Parameters.AddWithValue("id", SelfrolePanel.PanelId);
        cmd.Parameters.AddWithValue("channel", (long)channelId);
        cmd.Parameters.AddWithValue("message", (long)messageId);
        await cmd.ExecuteNonQueryAsync();

        Invalidate();
    }

    public static async Task SetPanelEnabledAsync(bool enabled)
    {
        await using var cmd = Db.CreateCommand("UPDATE selfrole_panel SET enabled = @enabled WHERE id = @id");
        cmd.Parameters.AddWithValue("id", SelfrolePanel.PanelId);
        cmd.Parameters.AddWithValue("enabled", enabled);
        await cmd.ExecuteNonQueryAsync();

        Invalidate();
    }

    public static async Task SetRenderedHashAsync(string hash)
    {
        await using var cmd = Db.CreateCommand("UPDATE selfrole_panel SET rendered_hash = @hash WHERE id = @id");
        cmd.Parameters.AddWithValue("id", SelfrolePanel.PanelId);
        cmd.Parameters.AddWithValue("hash", hash);
        await cmd.ExecuteNonQueryAsync();

        Invalidate();
    }

    public static async Task UpsertCategoryAsync(SelfroleCategory category)
    {
        await using (var update = Db.CreateCommand(
                         "UPDATE selfrole_categories SET name = @name, emoji = @emoji, " +
                         "button_style = @buttonStyle, kind = @kind, placeholder = @placeholder, " +
                         "min_values = @minValues, max_values = @maxValues, allow_clear = @allowClear, " +
                         "sticky = @sticky, auto_assign = @autoAssign, position = @position, " +
                         "enabled = @enabled WHERE id = @id"))
        {
            BindCategory(update, category);
            if (await update.ExecuteNonQueryAsync() > 0)
            {
                Invalidate();
                return;
            }
        }

        await using var insert = Db.CreateCommand(
            "INSERT INTO selfrole_categories (id, name, emoji, button_style, kind, placeholder, min_values, " +
            "max_values, allow_clear, sticky, auto_assign, position, enabled) " +
            "VALUES (@id, @name, @emoji, @buttonStyle, @kind, @placeholder, @minValues, @maxValues, " +
            "@allowClear, @sticky, @autoAssign, @position, @enabled)");
        BindCategory(insert, category);
        await insert.ExecuteNonQueryAsync();

        Invalidate();
    }

    private static void BindCategory(NpgsqlCommand cmd, SelfroleCategory category)
    {
        cmd.Parameters.AddWithValue("id", category.Id);
        cmd.Parameters.AddWithValue("name", category.Name ?? "");
        cmd.Parameters.AddWithValue("emoji", category.Emoji ?? "");
        cmd.Parameters.AddWithValue("buttonStyle", category.ButtonStyle);
        cmd.Parameters.AddWithValue("kind", category.Kind);
        cmd.Parameters.AddWithValue("placeholder", category.Placeholder ?? "");
        cmd.Parameters.AddWithValue("minValues", category.MinValues);
        cmd.Parameters.AddWithValue("maxValues", category.MaxValues);
        cmd.Parameters.AddWithValue("allowClear", category.AllowClear);
        cmd.Parameters.AddWithValue("sticky", category.Sticky);
        cmd.Parameters.AddWithValue("autoAssign", category.AutoAssign);
        cmd.Parameters.AddWithValue("position", category.Position);
        cmd.Parameters.AddWithValue("enabled", category.Enabled);
    }

    public static async Task DeleteCategoryAsync(string categoryId)
    {
        await using (var options = Db.CreateCommand("DELETE FROM selfrole_options WHERE category_id = @id"))
        {
            options.Parameters.AddWithValue("id", categoryId);
            await options.ExecuteNonQueryAsync();
        }

        await using var cmd = Db.CreateCommand("DELETE FROM selfrole_categories WHERE id = @id");
        cmd.Parameters.AddWithValue("id", categoryId);
        await cmd.ExecuteNonQueryAsync();

        Invalidate();
    }

    public static async Task<bool> CategoryExistsAsync(string categoryId)
    {
        await using var cmd = Db.CreateCommand(
            "SELECT EXISTS (SELECT 1 FROM selfrole_categories WHERE id = @id)");
        cmd.Parameters.AddWithValue("id", categoryId);
        return await cmd.ExecuteScalarAsync() is true;
    }

    public static async Task UpsertOptionAsync(SelfroleOption option)
    {
        await using (var update = Db.CreateCommand(
                         "UPDATE selfrole_options SET role_id = @role, label = @label, " +
                         "description = @description, emoji = @emoji, match_patterns = @patterns, " +
                         "position = @position, enabled = @enabled WHERE category_id = @category AND id = @id"))
        {
            BindOption(update, option);
            if (await update.ExecuteNonQueryAsync() > 0)
            {
                Invalidate();
                return;
            }
        }

        await using var insert = Db.CreateCommand(
            "INSERT INTO selfrole_options (id, category_id, role_id, label, description, emoji, " +
            "match_patterns, position, enabled) " +
            "VALUES (@id, @category, @role, @label, @description, @emoji, @patterns, @position, @enabled)");
        BindOption(insert, option);
        await insert.ExecuteNonQueryAsync();

        Invalidate();
    }

    private static void BindOption(NpgsqlCommand cmd, SelfroleOption option)
    {
        cmd.Parameters.AddWithValue("id", option.Id);
        cmd.Parameters.AddWithValue("category", option.CategoryId);
        cmd.Parameters.AddWithValue("role", (long)option.RoleId);
        cmd.Parameters.AddWithValue("label", option.Label ?? "");
        cmd.Parameters.AddWithValue("description", option.Description ?? "");
        cmd.Parameters.AddWithValue("emoji", option.Emoji ?? "");
        cmd.Parameters.AddWithValue("patterns", option.MatchPatterns ?? []);
        cmd.Parameters.AddWithValue("position", option.Position);
        cmd.Parameters.AddWithValue("enabled", option.Enabled);
    }

    public static async Task DeleteOptionAsync(string categoryId, string optionId)
    {
        await using var cmd = Db.CreateCommand(
            "DELETE FROM selfrole_options WHERE category_id = @category AND id = @id");
        cmd.Parameters.AddWithValue("category", categoryId);
        cmd.Parameters.AddWithValue("id", optionId);
        await cmd.ExecuteNonQueryAsync();

        Invalidate();
    }

    #endregion

    #region Roles

    /// <summary>Which options of this category the member holds right now.</summary>
    public static HashSet<string> HeldOptionIds(DiscordMember member, SelfroleCategory category)
    {
        return
        [
            .. category.ActiveOptions
                .Where(option => member.Roles.Any(role => role.Id == option.RoleId))
                .Select(option => option.Id)
        ];
    }

    /// <summary>
    ///     Applies a selection that came from one rendered component. <paramref name="scope" /> is what
    ///     that component showed, not the whole category: a category split across two selects must not
    ///     lose the roles of the block the member did not touch.
    /// </summary>
    public static async Task<SelfroleChange> ApplySelectionAsync(DiscordMember member, SelfroleCategory category,
        IReadOnlyList<SelfroleOption> scope, IReadOnlyCollection<string> selectedOptionIds)
    {
        var desired = scope.Where(option => selectedOptionIds.Contains(option.Id)).ToList();
        var keep = category.ActiveOptions
            .Where(option => scope.All(s => s.Id != option.Id))
            .Where(option => member.Roles.Any(role => role.Id == option.RoleId));

        return await ApplyAsync(member, category, [.. desired.Concat(keep)]);
    }

    /// <summary>Flips one option. An exclusive category swaps instead of stacking.</summary>
    public static async Task<SelfroleChange> ApplyToggleAsync(DiscordMember member, SelfroleCategory category,
        SelfroleOption option)
    {
        var active = member.Roles.Any(role => role.Id == option.RoleId);
        if (active)
        {
            var remaining = category.ActiveOptions
                .Where(o => o.Id != option.Id)
                .Where(o => member.Roles.Any(role => role.Id == o.RoleId))
                .ToList();

            if (remaining.Count < category.MinValues)
                return SelfroleChange.Rejected(category.IsExclusive
                    ? $"Aus **{category.Name}** kannst du nichts entfernen, nur wechseln."
                    : $"Du brauchst mindestens {category.MinValues} Auswahl aus **{category.Name}**.");

            return await ApplyAsync(member, category, remaining);
        }

        if (category.IsExclusive) return await ApplyAsync(member, category, [option]);

        var target = category.ActiveOptions
            .Where(o => member.Roles.Any(role => role.Id == o.RoleId))
            .Append(option)
            .ToList();

        return await ApplyAsync(member, category, target);
    }

    /// <summary>
    ///     Empties the category. Unlike unticking a single option this is a reset, not a refusal, so the
    ///     automatic detection is allowed to hand these roles out again afterwards.
    /// </summary>
    public static async Task<SelfroleChange> ClearAsync(DiscordMember member, SelfroleCategory category)
    {
        if (!category.AllowClear || category.MinValues > 0)
            return SelfroleChange.Rejected($"Aus **{category.Name}** lässt sich nicht alles entfernen.");

        var change = await ApplyAsync(member, category, []);
        if (change.Rejection is null) await ForgetAutoAssignAsync(member.Id, category);

        return change;
    }

    /// <summary>Drops the "already handed out" marks of a category, so detection starts over for it.</summary>
    private static async Task ForgetAutoAssignAsync(ulong userId, SelfroleCategory category)
    {
        var optionIds = category.Options.Select(option => option.Id).ToArray();
        if (optionIds.Length == 0) return;

        await using var cmd = Db.CreateCommand(
            "DELETE FROM selfrole_autoassign WHERE user_id = @user AND option_id = ANY(@options)");
        cmd.Parameters.AddWithValue("user", (long)userId);
        cmd.Parameters.AddWithValue("options", optionIds);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    ///     Brings the member's roles in line with <paramref name="target" /> for this category and leaves
    ///     everything else alone. One request instead of one per role, which is what made the Python
    ///     version take twenty seconds to hand out twenty game roles.
    /// </summary>
    private static async Task<SelfroleChange> ApplyAsync(DiscordMember member, SelfroleCategory category,
        IReadOnlyList<SelfroleOption> target)
    {
        var guild = CurrentApplication.TargetGuild;
        if (guild is null) return SelfroleChange.Rejected("Der Server ist gerade nicht erreichbar.");

        var self = guild.CurrentMember;
        if (self is null) return SelfroleChange.Rejected("Der Server ist gerade nicht erreichbar.");

        var max = category.ResolveMaxValues(category.ActiveOptions.Count);
        if (target.Count > max)
            return SelfroleChange.Rejected($"Aus **{category.Name}** sind höchstens {max} Rollen möglich.");

        // Discord enforces the minimum inside one menu, but not across a category split over several.
        if (target.Count < category.MinValues)
            return SelfroleChange.Rejected(
                $"Aus **{category.Name}** brauchst du mindestens {category.MinValues} Auswahl.");

        var limit = self.Hierarchy;
        var current = member.Roles.Select(role => role.Id).ToHashSet();
        var wanted = target.Select(option => option.RoleId).ToHashSet();

        var added = new List<string>();
        var removed = new List<string>();
        var skipped = new List<string>();
        var roles = member.Roles.Where(role => category.ActiveOptions.All(o => o.RoleId != role.Id)).ToList();

        foreach (var option in category.ActiveOptions)
        {
            var role = guild.GetRole(option.RoleId);
            var has = current.Contains(option.RoleId);
            var want = wanted.Contains(option.RoleId);

            if (role is null)
            {
                if (has) skipped.Add(option.Label);
                continue;
            }

            if (role.Position >= limit)
            {
                if (has) roles.Add(role);
                if (has != want) skipped.Add(option.Label);
                continue;
            }

            if (want) roles.Add(role);
            if (want && !has) added.Add(option.Label);
            if (!want && has) removed.Add(option.Label);
        }

        if (added.Count > 0 || removed.Count > 0)
            await member.ReplaceRolesAsync(roles, AuditReason);

        var held = category.ActiveOptions
            .Where(option => roles.Any(role => role.Id == option.RoleId))
            .Select(option => option.Id)
            .ToHashSet();

        return new SelfroleChange(added, removed, skipped, null, held);
    }

    #endregion

    #region Sticky

    /// <summary>
    ///     Safety net for the leave event. <see cref="SyncMemberRolesAsync" /> keeps the set current while
    ///     the member is here, this catches whatever a missed update left behind.
    /// </summary>
    public static async Task SaveStickyAsync(DiscordMember member)
    {
        var panel = await GetPanelAsync();
        var held = panel.Categories
            .Where(category => category.Sticky)
            .SelectMany(category => category.ActiveOptions)
            .Where(option => member.Roles.Any(role => role.Id == option.RoleId))
            .Select(option => option.Id)
            .ToList();

        if (held.Count == 0) return;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var optionId in held)
        {
            await using var cmd = Db.CreateCommand(
                "INSERT INTO selfrole_sticky (user_id, option_id, saved_at) VALUES (@user, @option, @at) " +
                "ON CONFLICT (user_id, option_id) DO UPDATE SET saved_at = EXCLUDED.saved_at");
            cmd.Parameters.AddWithValue("user", (long)member.Id);
            cmd.Parameters.AddWithValue("option", optionId);
            cmd.Parameters.AddWithValue("at", now);
            await cmd.ExecuteNonQueryAsync();
        }

        CurrentApplication.Logger.Information("Selfroles: {Count} Rollen von {User} gemerkt", held.Count, member.Id);
    }

    /// <summary>
    ///     Gives a returning member their roles back. The snapshot is kept, so someone who leaves and
    ///     rejoins twice in a row does not end up empty; the retention window clears it instead.
    /// </summary>
    public static async Task RestoreStickyAsync(DiscordMember member)
    {
        var saved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = Db.CreateCommand("SELECT option_id FROM selfrole_sticky WHERE user_id = @user"))
        {
            cmd.Parameters.AddWithValue("user", (long)member.Id);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) saved.Add(reader.GetString(0));
        }

        if (saved.Count == 0) return;

        var guild = CurrentApplication.TargetGuild;
        if (guild?.CurrentMember is null) return;

        var limit = guild.CurrentMember.Hierarchy;
        var panel = await GetPanelAsync();
        var roles = member.Roles.ToList();
        var restored = 0;

        foreach (var option in panel.Categories.Where(c => c.Sticky).SelectMany(c => c.ActiveOptions))
        {
            if (!saved.Contains(option.Id)) continue;
            if (roles.Any(role => role.Id == option.RoleId)) continue;

            var role = guild.GetRole(option.RoleId);
            if (role is null || role.Position >= limit)
            {
                CurrentApplication.Logger.Warning(
                    "Selfroles: {Option} konnte fuer {User} nicht wiederhergestellt werden", option.Id, member.Id);
                continue;
            }

            roles.Add(role);
            restored++;
        }

        if (restored == 0) return;

        await member.ReplaceRolesAsync(roles, "Selfroles | Wiederbeitritt");
        CurrentApplication.Logger.Information("Selfroles: {Count} Rollen fuer {User} wiederhergestellt", restored,
            member.Id);
    }

    /// <summary>
    ///     Keeps the remembered set in step with what a member actually holds, whoever changed it. A role a
    ///     teammate hands out by hand counts the same as one picked in the menu: it comes back on a rejoin,
    ///     and the automatic detection treats it as already given, so dropping it again sticks.
    /// </summary>
    public static async Task SyncMemberRolesAsync(ulong userId, IReadOnlyCollection<ulong> added,
        IReadOnlyCollection<ulong> removed)
    {
        if (added.Count == 0 && removed.Count == 0) return;

        var panel = await GetPanelAsync();
        var options = panel.Categories
            .SelectMany(category => category.Options.Select(option => (Category: category, Option: option)))
            .ToList();

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        foreach (var (category, option) in options.Where(entry => added.Contains(entry.Option.RoleId)))
        {
            if (category.Sticky)
            {
                await using var sticky = Db.CreateCommand(
                    "INSERT INTO selfrole_sticky (user_id, option_id, saved_at) VALUES (@user, @option, @at) " +
                    "ON CONFLICT (user_id, option_id) DO UPDATE SET saved_at = EXCLUDED.saved_at");
                sticky.Parameters.AddWithValue("user", (long)userId);
                sticky.Parameters.AddWithValue("option", option.Id);
                sticky.Parameters.AddWithValue("at", now);
                await sticky.ExecuteNonQueryAsync();
            }

            await using var handed = Db.CreateCommand(
                "INSERT INTO selfrole_autoassign (user_id, option_id, assigned_at) VALUES (@user, @option, @at) " +
                "ON CONFLICT (user_id, option_id) DO NOTHING");
            handed.Parameters.AddWithValue("user", (long)userId);
            handed.Parameters.AddWithValue("option", option.Id);
            handed.Parameters.AddWithValue("at", now);
            await handed.ExecuteNonQueryAsync();
        }

        foreach (var (_, option) in options.Where(entry => removed.Contains(entry.Option.RoleId)))
        {
            await using var cmd = Db.CreateCommand(
                "DELETE FROM selfrole_sticky WHERE user_id = @user AND option_id = @option");
            cmd.Parameters.AddWithValue("user", (long)userId);
            cmd.Parameters.AddWithValue("option", option.Id);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public static async Task PurgeStickyAsync()
    {
        var days = await RuntimeSettings.GetIntAsync(Section, "StickyRetentionDays", 365);
        if (days <= 0) return;

        var cutoff = DateTimeOffset.UtcNow.AddDays(-days).ToUnixTimeSeconds();
        await using var cmd = Db.CreateCommand("DELETE FROM selfrole_sticky WHERE saved_at < @cutoff");
        cmd.Parameters.AddWithValue("cutoff", cutoff);
        var removed = await cmd.ExecuteNonQueryAsync();

        if (removed > 0)
            CurrentApplication.Logger.Information("Selfroles: {Count} gemerkte Rollen verfallen", removed);
    }

    public static async Task<int> CountStickyMembersAsync()
    {
        await using var cmd = Db.CreateCommand("SELECT COUNT(DISTINCT user_id) FROM selfrole_sticky");
        return await cmd.ExecuteScalarAsync() is long count ? (int)count : 0;
    }

    #endregion

    #region Auto detection

    public static Task<bool> AutoDetectEnabledAsync()
    {
        return RuntimeSettings.GetBoolAsync(Section, "AutoDetect", false);
    }

    public static async Task<bool> IsOptedOutAsync(ulong userId)
    {
        await using var cmd = Db.CreateCommand("SELECT EXISTS (SELECT 1 FROM selfrole_optout WHERE user_id = @user)");
        cmd.Parameters.AddWithValue("user", (long)userId);
        return await cmd.ExecuteScalarAsync() is true;
    }

    public static async Task SetOptOutAsync(ulong userId, bool optedOut)
    {
        if (optedOut)
        {
            await using var insert = Db.CreateCommand(
                "INSERT INTO selfrole_optout (user_id, opted_out_at) VALUES (@user, @at) " +
                "ON CONFLICT (user_id) DO NOTHING");
            insert.Parameters.AddWithValue("user", (long)userId);
            insert.Parameters.AddWithValue("at", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            await insert.ExecuteNonQueryAsync();
            return;
        }

        await using var delete = Db.CreateCommand("DELETE FROM selfrole_optout WHERE user_id = @user");
        delete.Parameters.AddWithValue("user", (long)userId);
        await delete.ExecuteNonQueryAsync();
    }

    /// <summary>
    ///     Hands out the role for the game a member just started. A catalogue binding decides it outright;
    ///     only when the game is unbound does the matcher get a say, and then just once, for the single
    ///     best candidate. Two options scoring the same means the guess is worthless, so nothing happens.
    ///     Only ever adds, and only once per option: whoever takes such a role off again keeps it off.
    /// </summary>
    public static async Task AutoAssignAsync(DiscordMember member, GameCatalogEntry game)
    {
        if (!await AutoDetectEnabledAsync()) return;
        if (await IsOptedOutAsync(member.Id)) return;

        var guild = CurrentApplication.TargetGuild;
        if (guild?.CurrentMember is null) return;

        var panel = await GetPanelAsync();
        var options = panel.Categories
            .Where(category => category is { Enabled: true, AutoAssign: true })
            .SelectMany(category => category.ActiveOptions)
            .ToList();

        var option = Resolve(game, options);
        if (option is null) return;
        if (member.Roles.Any(role => role.Id == option.RoleId)) return;

        if ((await LoadAutoAssignedAsync(member.Id)).Contains(option.Id)) return;

        var role = guild.GetRole(option.RoleId);
        if (role is null || role.Position >= guild.CurrentMember.Hierarchy) return;

        await member.ReplaceRolesAsync([.. member.Roles, role], "Selfroles | Automatisch erkannt");

        await using (var cmd = Db.CreateCommand(
                         "INSERT INTO selfrole_autoassign (user_id, option_id, assigned_at) " +
                         "VALUES (@user, @option, @at) ON CONFLICT (user_id, option_id) DO NOTHING"))
        {
            cmd.Parameters.AddWithValue("user", (long)member.Id);
            cmd.Parameters.AddWithValue("option", option.Id);
            cmd.Parameters.AddWithValue("at", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            await cmd.ExecuteNonQueryAsync();
        }

        CurrentApplication.Logger.Information("Selfroles: {Option} fuer {User} automatisch vergeben ({Game})",
            option.Id, member.Id, game.DisplayName);

        await NotifyAutoAssignAsync(member, option, game);
    }

    private static SelfroleOption? Resolve(GameCatalogEntry game, IReadOnlyList<SelfroleOption> options)
    {
        if (game.IsBound)
            return options.FirstOrDefault(option =>
                string.Equals(option.Id, game.OptionId, StringComparison.Ordinal));

        SelfroleOption? best = null;
        var bestScore = 0.0;
        var runnerUp = 0.0;

        foreach (var option in options.Where(option => option.MatchPatterns.Length > 0))
        {
            var score = ActivityMatcher.Score(option.MatchPatterns, game.DisplayName);
            if (score > bestScore)
            {
                runnerUp = bestScore;
                bestScore = score;
                best = option;
                continue;
            }

            if (score > runnerUp) runnerUp = score;
        }

        if (best is null || bestScore < ActivityMatcher.Threshold) return null;

        if (bestScore - runnerUp < ActivityMatcher.Margin)
        {
            CurrentApplication.Logger.Information(
                "Selfroles: {Game} passt auf mehrere Optionen, nichts vergeben", game.DisplayName);
            return null;
        }

        return best;
    }

    private static async Task<HashSet<string>> LoadAutoAssignedAsync(ulong userId)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = Db.CreateCommand("SELECT option_id FROM selfrole_autoassign WHERE user_id = @user");
        cmd.Parameters.AddWithValue("user", (long)userId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(reader.GetString(0));

        return result;
    }

    /// <summary>
    ///     Once per member, so nobody first notices the feature by wondering where a role came from.
    /// </summary>
    private static async Task NotifyAutoAssignAsync(DiscordMember member, SelfroleOption option,
        GameCatalogEntry game)
    {
        if (!await RuntimeSettings.GetBoolAsync(Section, "AutoDetectNotify", true)) return;

        await using (var check = Db.CreateCommand(
                         "SELECT COUNT(*) FROM selfrole_autoassign WHERE user_id = @user"))
        {
            check.Parameters.AddWithValue("user", (long)member.Id);
            if (await check.ExecuteScalarAsync() is long count && count > 1) return;
        }

        try
        {
            var settings = ToolSet.GetDashboardUrl("benutzereinstellungen");
            var embed = new DiscordEmbedBuilder()
                .WithTitle("Rolle automatisch vergeben")
                .WithDescription($"Weil du gerade **{game.DisplayName}** gespielt hast, habe ich dir die Rolle " +
                                 $"**{option.Label}** gegeben.\n\nAbwählen kannst du sie jederzeit im " +
                                 "Selfrole-Panel. Die automatische Erkennung selbst schaltest du in " +
                                 $"[deinen Einstellungen]({settings}) ab.")
                .WithColor(BotConfig.GetEmbedColor());

            await member.SendMessageAsync(embed);
        }
        catch (Exception e)
        {
            CurrentApplication.Logger.Debug(e, "Selfroles: Hinweis an {User} nicht zustellbar", member.Id);
        }
    }

    #endregion

    #region Stats

    /// <summary>One row per option and day, so the dashboard can show where a role is going.</summary>
    public static async Task CaptureStatsAsync()
    {
        var guild = CurrentApplication.TargetGuild;
        if (guild is null) return;

        var panel = await GetPanelAsync();
        var today = DateTime.UtcNow.Date;

        foreach (var option in panel.Categories.SelectMany(category => category.Options))
        {
            var count = guild.Members.Values.Count(member => member.Roles.Any(role => role.Id == option.RoleId));

            await using var cmd = Db.CreateCommand(
                "INSERT INTO selfrole_stats (option_id, day, member_count) VALUES (@option, @day, @count) " +
                "ON CONFLICT (option_id, day) DO UPDATE SET member_count = EXCLUDED.member_count");
            cmd.Parameters.AddWithValue("option", option.Id);
            cmd.Parameters.AddWithValue("day", today);
            cmd.Parameters.AddWithValue("count", count);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    /// <summary>Members per option right now, and how that compares to the snapshot 30 days back.</summary>
    public static async Task<Dictionary<string, SelfroleUsage>> GetUsageAsync(SelfroleCategory category)
    {
        var result = new Dictionary<string, SelfroleUsage>(StringComparer.OrdinalIgnoreCase);
        var guild = CurrentApplication.TargetGuild;
        var past = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        await using (var cmd = Db.CreateCommand(
                         "SELECT DISTINCT ON (option_id) option_id, member_count FROM selfrole_stats " +
                         "WHERE option_id = ANY(@ids) AND day <= @cutoff ORDER BY option_id, day DESC"))
        {
            cmd.Parameters.AddWithValue("ids", category.Options.Select(option => option.Id).ToArray());
            cmd.Parameters.AddWithValue("cutoff", DateTime.UtcNow.Date.AddDays(-30));
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) past[reader.GetString(0)] = reader.GetInt32(1);
        }

        foreach (var option in category.Options)
        {
            var current = guild?.Members.Values.Count(member => member.Roles.Any(role => role.Id == option.RoleId));
            result[option.Id] = new SelfroleUsage(current ?? 0,
                past.TryGetValue(option.Id, out var before) ? current - before : null);
        }

        return result;
    }

    #endregion
}

/// <summary>What one interaction actually changed, so the menu can say it instead of staying silent.</summary>
public sealed record SelfroleChange(
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Removed,
    IReadOnlyList<string> Skipped,
    string? Rejection,
    IReadOnlySet<string>? Held)
{
    public static SelfroleChange Rejected(string reason)
    {
        return new SelfroleChange([], [], [], reason, null);
    }
}

/// <summary>Members holding an option's role now, and the change against the snapshot 30 days back.</summary>
public sealed record SelfroleUsage(int Current, int? Change);
