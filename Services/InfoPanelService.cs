#region

using AGC_Management.Entities.InfoPanels;

#endregion

namespace AGC_Management.Services;

/// <summary>
///     Reads and writes the info panels. A panel is hit on every component interaction, so the whole set
///     is cached for a short while, the same way <see cref="TicketCategoryService" /> does it.
/// </summary>
public static class InfoPanelService
{
    /// <summary>The only panel that ships today. Selfroles would be a second row in the same tables.</summary>
    public const string RulesPanelId = "regelwerk";

    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
    private static readonly SemaphoreSlim LoadLock = new(1, 1);

    private static List<InfoPanel>? _cache;
    private static DateTime _cacheExpires = DateTime.MinValue;

    private static NpgsqlDataSource Db =>
        CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

    public static void Invalidate()
    {
        _cacheExpires = DateTime.MinValue;
    }

    public static async Task<List<InfoPanel>> GetAllAsync()
    {
        var all = await LoadAsync();
        return [.. all.OrderBy(panel => panel.SortOrder).ThenBy(panel => panel.Name)];
    }

    public static async Task<InfoPanel?> GetAsync(string? panelId)
    {
        if (string.IsNullOrWhiteSpace(panelId)) return null;
        var all = await LoadAsync();
        return all.FirstOrDefault(panel => string.Equals(panel.Id, panelId, StringComparison.OrdinalIgnoreCase));
    }

    public static async Task<InfoPanelPage?> GetPageAsync(string panelId, string groupId, string pageId)
    {
        var panel = await GetAsync(panelId);
        var group = panel?.Groups.FirstOrDefault(g => g.Id == groupId);
        return group?.Pages.FirstOrDefault(page => page.Id == pageId);
    }

    private static async Task<List<InfoPanel>> LoadAsync()
    {
        if (_cache is not null && _cacheExpires > DateTime.UtcNow) return _cache;

        await LoadLock.WaitAsync();
        try
        {
            if (_cache is not null && _cacheExpires > DateTime.UtcNow) return _cache;

            var panels = new List<InfoPanel>();
            await using (var cmd = Db.CreateCommand(
                             "SELECT id, name, channel_id, message_id, enabled, header_title, header_text, " +
                             "author_name, author_icon_mode, author_icon_url, banner_mode, banner_url, " +
                             "color, auto_repost, rendered_hash, sort_order FROM infopanels ORDER BY sort_order, id"))
            {
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    panels.Add(new InfoPanel
                    {
                        Id = reader.GetString(0),
                        Name = Str(reader, 1),
                        ChannelId = reader.IsDBNull(2) ? 0 : (ulong)reader.GetInt64(2),
                        MessageId = reader.IsDBNull(3) ? 0 : (ulong)reader.GetInt64(3),
                        Enabled = !reader.IsDBNull(4) && reader.GetBoolean(4),
                        HeaderTitle = Str(reader, 5),
                        HeaderText = Str(reader, 6),
                        AuthorName = Str(reader, 7),
                        AuthorIconMode = Str(reader, 8, InfoPanel.ModeGuild),
                        AuthorIconUrl = Str(reader, 9),
                        BannerMode = Str(reader, 10, InfoPanel.ModeGuild),
                        BannerUrl = Str(reader, 11),
                        Color = Str(reader, 12, "2F3136"),
                        AutoRepost = reader.IsDBNull(13) || reader.GetBoolean(13),
                        RenderedHash = Str(reader, 14),
                        SortOrder = reader.IsDBNull(15) ? 0 : reader.GetInt32(15)
                    });
            }

            var groups = await LoadAllGroupsAsync();
            var pages = await LoadAllPagesAsync();

            foreach (var panel in panels)
            {
                if (!groups.TryGetValue(panel.Id, out var own)) continue;

                panel.Groups = own;
                foreach (var group in panel.Groups)
                    if (pages.TryGetValue(group.Id, out var groupPages))
                        group.Pages = groupPages;
            }

            _cache = panels;
            _cacheExpires = DateTime.UtcNow + CacheTtl;
            return panels;
        }
        finally
        {
            LoadLock.Release();
        }
    }

    private static string Str(NpgsqlDataReader reader, int ordinal, string fallback = "")
    {
        if (reader.IsDBNull(ordinal)) return fallback;
        var value = reader.GetString(ordinal);
        return string.IsNullOrEmpty(value) ? fallback : value;
    }

    private static async Task<Dictionary<string, List<InfoPanelGroup>>> LoadAllGroupsAsync()
    {
        var result = new Dictionary<string, List<InfoPanelGroup>>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = Db.CreateCommand(
            "SELECT id, panel_id, kind, placeholder, date_label, position, enabled " +
            "FROM infopanel_groups ORDER BY panel_id, position");
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var group = new InfoPanelGroup
            {
                Id = reader.GetString(0),
                PanelId = reader.GetString(1),
                Kind = Str(reader, 2, InfoPanelGroup.KindSelect),
                Placeholder = Str(reader, 3),
                DateLabel = Str(reader, 4),
                Position = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                Enabled = reader.IsDBNull(6) || reader.GetBoolean(6)
            };

            if (!result.TryGetValue(group.PanelId, out var list))
            {
                list = [];
                result[group.PanelId] = list;
            }

            list.Add(group);
        }

        return result;
    }

    private static async Task<Dictionary<string, List<InfoPanelPage>>> LoadAllPagesAsync()
    {
        var result = new Dictionary<string, List<InfoPanelPage>>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = Db.CreateCommand(
            "SELECT id, group_id, panel_id, kind, label, description, emoji, button_style, url, title, content, " +
            "image_url, color, position, enabled, updated_at FROM infopanel_pages ORDER BY group_id, position");
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var page = new InfoPanelPage
            {
                Id = reader.GetString(0),
                GroupId = reader.GetString(1),
                PanelId = reader.GetString(2),
                Kind = Str(reader, 3, InfoPanelPage.KindText),
                Label = Str(reader, 4),
                Description = Str(reader, 5),
                Emoji = Str(reader, 6),
                ButtonStyle = reader.IsDBNull(7) ? (int)ButtonStyle.Primary : reader.GetInt32(7),
                Url = Str(reader, 8),
                Title = Str(reader, 9),
                Content = Str(reader, 10),
                ImageUrl = Str(reader, 11),
                Color = Str(reader, 12),
                Position = reader.IsDBNull(13) ? 0 : reader.GetInt32(13),
                Enabled = reader.IsDBNull(14) || reader.GetBoolean(14),
                UpdatedAt = reader.IsDBNull(15) ? 0 : reader.GetInt64(15)
            };

            if (!result.TryGetValue(page.GroupId, out var list))
            {
                list = [];
                result[page.GroupId] = list;
            }

            list.Add(page);
        }

        return result;
    }

    /// <summary>
    ///     Writes the panel row. Update-then-insert rather than ON CONFLICT, for the same reason
    ///     <see cref="TicketCategoryService.UpsertAsync" /> does it: it still works on a database whose
    ///     unique index could not be created.
    /// </summary>
    public static async Task UpsertAsync(InfoPanel panel)
    {
        await using (var update = Db.CreateCommand(
                         "UPDATE infopanels SET name = @name, channel_id = @channel, message_id = @message, " +
                         "enabled = @enabled, header_title = @headerTitle, header_text = @headerText, " +
                         "author_name = @authorName, author_icon_mode = @authorIconMode, " +
                         "author_icon_url = @authorIconUrl, banner_mode = @bannerMode, banner_url = @bannerUrl, " +
                         "color = @color, auto_repost = @autoRepost, " +
                         "rendered_hash = @hash, sort_order = @sort WHERE id = @id"))
        {
            Bind(update, panel);
            if (await update.ExecuteNonQueryAsync() > 0)
            {
                Invalidate();
                return;
            }
        }

        await using var insert = Db.CreateCommand(
            "INSERT INTO infopanels (id, name, channel_id, message_id, enabled, header_title, header_text, " +
            "author_name, author_icon_mode, author_icon_url, banner_mode, banner_url, color, " +
            "auto_repost, rendered_hash, sort_order) " +
            "VALUES (@id, @name, @channel, @message, @enabled, @headerTitle, @headerText, @authorName, " +
            "@authorIconMode, @authorIconUrl, @bannerMode, @bannerUrl, @color, @autoRepost, " +
            "@hash, @sort)");
        Bind(insert, panel);
        await insert.ExecuteNonQueryAsync();

        Invalidate();
    }

    private static void Bind(NpgsqlCommand cmd, InfoPanel panel)
    {
        BindSettings(cmd, panel);
        cmd.Parameters.AddWithValue("channel", (long)panel.ChannelId);
        cmd.Parameters.AddWithValue("message", (long)panel.MessageId);
        cmd.Parameters.AddWithValue("enabled", panel.Enabled);
        cmd.Parameters.AddWithValue("hash", panel.RenderedHash ?? "");
    }

    /// <summary>Only the parameters the settings update actually names.</summary>
    private static void BindSettings(NpgsqlCommand cmd, InfoPanel panel)
    {
        cmd.Parameters.AddWithValue("id", panel.Id);
        cmd.Parameters.AddWithValue("name", panel.Name ?? "");
        cmd.Parameters.AddWithValue("headerTitle", panel.HeaderTitle ?? "");
        cmd.Parameters.AddWithValue("headerText", panel.HeaderText ?? "");
        cmd.Parameters.AddWithValue("authorName", panel.AuthorName ?? "");
        cmd.Parameters.AddWithValue("authorIconMode", panel.AuthorIconMode ?? InfoPanel.ModeNone);
        cmd.Parameters.AddWithValue("authorIconUrl", panel.AuthorIconUrl ?? "");
        cmd.Parameters.AddWithValue("bannerMode", panel.BannerMode ?? InfoPanel.ModeNone);
        cmd.Parameters.AddWithValue("bannerUrl", panel.BannerUrl ?? "");
        cmd.Parameters.AddWithValue("color", string.IsNullOrWhiteSpace(panel.Color) ? "2F3136" : panel.Color);
        cmd.Parameters.AddWithValue("autoRepost", panel.AutoRepost);
        cmd.Parameters.AddWithValue("sort", panel.SortOrder);
    }

    /// <summary>
    ///     Writes only what the settings page owns. Channel, message id and hash are left alone on purpose:
    ///     the dashboard holds a copy that is seconds old, and the refresh task may have reposted the
    ///     message in the meantime. Saving a text must not send the panel back to a message that is gone.
    /// </summary>
    public static async Task UpdateSettingsAsync(InfoPanel panel)
    {
        await using var cmd = Db.CreateCommand(
            "UPDATE infopanels SET name = @name, header_title = @headerTitle, header_text = @headerText, " +
            "author_name = @authorName, author_icon_mode = @authorIconMode, author_icon_url = @authorIconUrl, " +
            "banner_mode = @bannerMode, banner_url = @bannerUrl, color = @color, " +
            "auto_repost = @autoRepost, sort_order = @sort WHERE id = @id");
        BindSettings(cmd, panel);
        await cmd.ExecuteNonQueryAsync();

        Invalidate();
    }

    public static async Task SetEnabledAsync(string panelId, bool enabled)
    {
        await using var cmd = Db.CreateCommand("UPDATE infopanels SET enabled = @enabled WHERE id = @id");
        cmd.Parameters.AddWithValue("id", panelId);
        cmd.Parameters.AddWithValue("enabled", enabled);
        await cmd.ExecuteNonQueryAsync();

        Invalidate();
    }

    /// <summary>Stores where the panel lives now. Separate from <see cref="UpsertAsync" /> so posting
    ///     does not have to write the whole row back.</summary>
    public static async Task SetLocationAsync(string panelId, ulong channelId, ulong messageId)
    {
        await using var cmd = Db.CreateCommand(
            "UPDATE infopanels SET channel_id = @channel, message_id = @message WHERE id = @id");
        cmd.Parameters.AddWithValue("id", panelId);
        cmd.Parameters.AddWithValue("channel", (long)channelId);
        cmd.Parameters.AddWithValue("message", (long)messageId);
        await cmd.ExecuteNonQueryAsync();

        Invalidate();
    }

    public static async Task SetRenderedHashAsync(string panelId, string hash)
    {
        await using var cmd = Db.CreateCommand("UPDATE infopanels SET rendered_hash = @hash WHERE id = @id");
        cmd.Parameters.AddWithValue("id", panelId);
        cmd.Parameters.AddWithValue("hash", hash);
        await cmd.ExecuteNonQueryAsync();

        Invalidate();
    }

    public static async Task UpsertGroupAsync(InfoPanelGroup group)
    {
        await using (var update = Db.CreateCommand(
                         "UPDATE infopanel_groups SET kind = @kind, placeholder = @placeholder, " +
                         "date_label = @dateLabel, position = @position, enabled = @enabled " +
                         "WHERE panel_id = @panel AND id = @id"))
        {
            BindGroup(update, group);
            if (await update.ExecuteNonQueryAsync() > 0)
            {
                Invalidate();
                return;
            }
        }

        await using var insert = Db.CreateCommand(
            "INSERT INTO infopanel_groups (id, panel_id, kind, placeholder, date_label, position, enabled) " +
            "VALUES (@id, @panel, @kind, @placeholder, @dateLabel, @position, @enabled)");
        BindGroup(insert, group);
        await insert.ExecuteNonQueryAsync();

        Invalidate();
    }

    private static void BindGroup(NpgsqlCommand cmd, InfoPanelGroup group)
    {
        cmd.Parameters.AddWithValue("id", group.Id);
        cmd.Parameters.AddWithValue("panel", group.PanelId);
        cmd.Parameters.AddWithValue("kind", group.Kind);
        cmd.Parameters.AddWithValue("placeholder", group.Placeholder ?? "");
        cmd.Parameters.AddWithValue("dateLabel", group.DateLabel ?? "");
        cmd.Parameters.AddWithValue("position", group.Position);
        cmd.Parameters.AddWithValue("enabled", group.Enabled);
    }

    /// <summary>
    ///     Writes a page. <c>updated_at</c> only moves when the title or the content actually changed,
    ///     otherwise every save would push the panel's "Stand der Regeln" date to today for nothing.
    /// </summary>
    public static async Task UpsertPageAsync(InfoPanelPage page)
    {
        var existing = await GetPageAsync(page.PanelId, page.GroupId, page.Id);
        var contentChanged = existing is null ||
                             !string.Equals(existing.Title, page.Title, StringComparison.Ordinal) ||
                             !string.Equals(existing.Content, page.Content, StringComparison.Ordinal);

        if (contentChanged) page.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        else if (page.UpdatedAt == 0) page.UpdatedAt = existing?.UpdatedAt ?? 0;

        await using (var update = Db.CreateCommand(
                         "UPDATE infopanel_pages SET panel_id = @panel, kind = @kind, label = @label, " +
                         "description = @description, emoji = @emoji, button_style = @buttonStyle, url = @url, " +
                         "title = @title, content = @content, image_url = @imageUrl, color = @color, " +
                         "position = @position, enabled = @enabled, updated_at = @updatedAt " +
                         "WHERE group_id = @group AND id = @id"))
        {
            BindPage(update, page);
            if (await update.ExecuteNonQueryAsync() > 0)
            {
                Invalidate();
                return;
            }
        }

        await using var insert = Db.CreateCommand(
            "INSERT INTO infopanel_pages (id, group_id, panel_id, kind, label, description, emoji, button_style, " +
            "url, title, content, image_url, color, position, enabled, updated_at) " +
            "VALUES (@id, @group, @panel, @kind, @label, @description, @emoji, @buttonStyle, @url, @title, " +
            "@content, @imageUrl, @color, @position, @enabled, @updatedAt)");
        BindPage(insert, page);
        await insert.ExecuteNonQueryAsync();

        Invalidate();
    }

    private static void BindPage(NpgsqlCommand cmd, InfoPanelPage page)
    {
        cmd.Parameters.AddWithValue("id", page.Id);
        cmd.Parameters.AddWithValue("group", page.GroupId);
        cmd.Parameters.AddWithValue("panel", page.PanelId);
        cmd.Parameters.AddWithValue("kind", page.Kind);
        cmd.Parameters.AddWithValue("label", page.Label ?? "");
        cmd.Parameters.AddWithValue("description", page.Description ?? "");
        cmd.Parameters.AddWithValue("emoji", page.Emoji ?? "");
        cmd.Parameters.AddWithValue("buttonStyle", page.ButtonStyle);
        cmd.Parameters.AddWithValue("url", page.Url ?? "");
        cmd.Parameters.AddWithValue("title", page.Title ?? "");
        cmd.Parameters.AddWithValue("content", page.Content ?? "");
        cmd.Parameters.AddWithValue("imageUrl", page.ImageUrl ?? "");
        cmd.Parameters.AddWithValue("color", page.Color ?? "");
        cmd.Parameters.AddWithValue("position", page.Position);
        cmd.Parameters.AddWithValue("enabled", page.Enabled);
        cmd.Parameters.AddWithValue("updatedAt", page.UpdatedAt);
    }

    /// <summary>Rewrites the order of a group's pages without touching their content or their dates.</summary>
    public static async Task SavePageOrderAsync(string groupId, IReadOnlyList<InfoPanelPage> pages)
    {
        var position = 0;
        foreach (var page in pages)
        {
            await using var cmd = Db.CreateCommand(
                "UPDATE infopanel_pages SET position = @position WHERE group_id = @group AND id = @id");
            cmd.Parameters.AddWithValue("group", groupId);
            cmd.Parameters.AddWithValue("id", page.Id);
            cmd.Parameters.AddWithValue("position", position++);
            await cmd.ExecuteNonQueryAsync();
        }

        Invalidate();
    }

    public static async Task DeletePageAsync(string groupId, string pageId)
    {
        await using var cmd = Db.CreateCommand("DELETE FROM infopanel_pages WHERE group_id = @group AND id = @id");
        cmd.Parameters.AddWithValue("group", groupId);
        cmd.Parameters.AddWithValue("id", pageId);
        await cmd.ExecuteNonQueryAsync();

        Invalidate();
    }

    public static async Task<bool> PageExistsAsync(string groupId, string pageId)
    {
        await using var cmd = Db.CreateCommand(
            "SELECT EXISTS (SELECT 1 FROM infopanel_pages WHERE group_id = @group AND id = @id)");
        cmd.Parameters.AddWithValue("group", groupId);
        cmd.Parameters.AddWithValue("id", pageId);
        return await cmd.ExecuteScalarAsync() is true;
    }
}
