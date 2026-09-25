#region

using System.Security.Cryptography;
using System.Text;
using AGC_Management.Entities.Selfroles;
using AGC_Management.Services;
using AGC_Management.Utils;
using DisCatSharp.Exceptions;

#endregion

namespace AGC_Management.Components;

/// <summary>
///     Builds the panel message and the per-member menu behind each of its buttons. The panel only
///     carries buttons on purpose: a shared message is the same for everyone, so it can never show a
///     viewer which roles they already hold. The menu is built for one member and can, which is what
///     makes "add one role" stop wiping the rest.
/// </summary>
public static class SelfroleComponents
{
    public const string Prefix = "srl:";

    public const string OpenPrefix = Prefix + "open:";
    public const string SelectPrefix = Prefix + "sel:";
    public const string TogglePrefix = Prefix + "tog:";
    public const string ClearPrefix = Prefix + "clr:";
    public const string AutoPrefix = Prefix + "auto:";

    private static readonly Timer RefreshTimer =
        new(_ => _ = RefreshPanelAsync(), null, Timeout.Infinite, Timeout.Infinite);

    /// <summary>Debounced: saving three fields in a row costs one edit, not three.</summary>
    public static void QueueRefresh()
    {
        RefreshTimer.Change(2000, Timeout.Infinite);
    }

    public static string OpenCustomId(SelfroleCategory category)
    {
        return OpenPrefix + category.Id;
    }

    public static string SelectCustomId(SelfroleCategory category, int chunk)
    {
        return $"{SelectPrefix}{category.Id}:{chunk}";
    }

    public static string ToggleCustomId(SelfroleCategory category, SelfroleOption option)
    {
        return $"{TogglePrefix}{category.Id}:{option.Id}";
    }

    public static string ClearCustomId(SelfroleCategory category)
    {
        return ClearPrefix + category.Id;
    }

    public static string AutoCustomId(SelfroleCategory category)
    {
        return AutoPrefix + category.Id;
    }

    #region Panel

    public static string ResolveBannerUrl(SelfrolePanel panel)
    {
        return panel.BannerMode switch
        {
            SelfrolePanel.ModeGuild => WithSize(CurrentApplication.TargetGuild?.BannerUrl, 1024),
            SelfrolePanel.ModeUrl => panel.BannerUrl ?? "",
            _ => ""
        };
    }

    public static string ResolveAuthorIconUrl(SelfrolePanel panel)
    {
        return panel.AuthorIconMode switch
        {
            SelfrolePanel.ModeGuild => CurrentApplication.TargetGuild?.IconUrl ?? "",
            SelfrolePanel.ModeUrl => panel.AuthorIconUrl ?? "",
            _ => ""
        };
    }

    private static string WithSize(string? url, int size)
    {
        if (string.IsNullOrWhiteSpace(url)) return "";
        return url.Contains('?') ? $"{url}&size={size}" : $"{url}?size={size}";
    }

    /// <summary>The categories that make it onto the panel, capped by what one message can hold.</summary>
    public static List<SelfroleCategory> PanelCategories(SelfrolePanel panel)
    {
        return
        [
            .. panel.Categories
                .Where(category => category.Enabled && category.ActiveOptions.Count > 0)
                .OrderBy(category => category.Position)
                .Take(DiscordLimits.ActionRowsPerMessage * DiscordLimits.ButtonsPerRow)
        ];
    }

    public static DiscordMessageBuilder BuildPanelMessage(SelfrolePanel panel)
    {
        var builder = new DiscordMessageBuilder().WithV2Components();
        var children = new List<DiscordComponent>();

        var bannerUrl = ResolveBannerUrl(panel);
        if (!string.IsNullOrWhiteSpace(bannerUrl))
            children.Add(new DiscordMediaGalleryComponent([new DiscordMediaGalleryItem(bannerUrl)]));

        var textDisplays = new List<DiscordTextDisplayComponent>();
        if (!string.IsNullOrWhiteSpace(panel.AuthorName))
            textDisplays.Add(new DiscordTextDisplayComponent(
                $"-# {panel.AuthorName.Truncate(DiscordLimits.EmbedAuthorName)}"));
        if (!string.IsNullOrWhiteSpace(panel.HeaderTitle))
            textDisplays.Add(
                new DiscordTextDisplayComponent($"# {panel.HeaderTitle.Truncate(DiscordLimits.EmbedTitle)}"));

        var description = InfoPanelTemplateResolver.Resolve(panel.HeaderText).Truncate(DiscordLimits.EmbedDescription);
        if (!string.IsNullOrWhiteSpace(description))
            textDisplays.Add(new DiscordTextDisplayComponent(description));

        if (textDisplays.Count > 0)
        {
            var iconUrl = ResolveAuthorIconUrl(panel);
            if (!string.IsNullOrWhiteSpace(iconUrl))
                children.Add(new DiscordSectionComponent(textDisplays).WithThumbnailComponent(iconUrl));
            else
                children.AddRange(textDisplays);
        }

        var rows = new List<DiscordActionRowComponent>();
        foreach (var chunk in PanelCategories(panel).Chunk(DiscordLimits.ButtonsPerRow))
            rows.Add(new DiscordActionRowComponent([
                .. chunk.Select(category => new DiscordButtonComponent(
                    ResolveButtonStyle(category.ButtonStyle),
                    OpenCustomId(category),
                    category.Name.Truncate(DiscordLimits.ButtonLabel),
                    false,
                    ComponentEmojiParser.Parse(category.Emoji)!))
            ]));

        if (rows.Count > 0)
        {
            if (children.Count > 0) children.Add(new DiscordSeparatorComponent());
            children.AddRange(rows);
        }

        builder.AddComponents([new DiscordContainerComponent(children, accentColor: panel.DiscordColor)]);

        return builder;
    }

    private static ButtonStyle ResolveButtonStyle(int style)
    {
        return Enum.IsDefined(typeof(ButtonStyle), style) && style != (int)ButtonStyle.Link
            ? (ButtonStyle)style
            : ButtonStyle.Secondary;
    }

    /// <summary>
    ///     Everything the panel message is built from, hashed. Option labels are deliberately absent:
    ///     they only ever show up in the ephemeral menu, so renaming one must not cost a message edit.
    /// </summary>
    public static string ComputeHash(SelfrolePanel panel)
    {
        var sb = new StringBuilder();
        sb.Append(panel.HeaderTitle).Append('\u001f')
            .Append(InfoPanelTemplateResolver.Resolve(panel.HeaderText)).Append('\u001f')
            .Append(panel.AuthorName).Append('\u001f')
            .Append(ResolveAuthorIconUrl(panel)).Append('\u001f')
            .Append(ResolveBannerUrl(panel)).Append('\u001f')
            .Append(panel.Color).Append('\u001e');

        foreach (var category in PanelCategories(panel))
            sb.Append(category.Id).Append('\u001f')
                .Append(category.Name).Append('\u001f')
                .Append(category.Emoji).Append('\u001f')
                .Append(category.ButtonStyle).Append('\u001e');

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    /// <summary>Posts the panel into a channel and remembers where it went. Used by the dashboard.</summary>
    public static async Task SendPanelToChannelAsync(ulong channelId)
    {
        var panel = await SelfroleService.GetPanelAsync();
        if (PanelCategories(panel).Count == 0)
            throw new InvalidOperationException("Es gibt keine aktive Kategorie mit Optionen.");

        var channel = await CurrentApplication.DiscordClient.GetChannelAsync(channelId);
        var message = await channel.SendMessageAsync(BuildPanelMessage(panel));

        await SelfroleService.SetPanelLocationAsync(message.ChannelId, message.Id);
        await SelfroleService.SetRenderedHashAsync(ComputeHash(panel));
        await SelfroleService.SetPanelEnabledAsync(true);
    }

    /// <summary>
    ///     Brings the posted message back in line with the database. Does nothing when the rendered result
    ///     is unchanged, so the periodic task costs no API calls on a quiet server.
    /// </summary>
    public static async Task<bool> RefreshPanelAsync(bool force = false)
    {
        try
        {
            SelfroleService.Invalidate();
            var panel = await SelfroleService.GetPanelAsync();
            if (!panel.IsPosted) return false;

            var hash = ComputeHash(panel);
            if (!force && hash == panel.RenderedHash) return true;

            var builder = BuildPanelMessage(panel);
            var channel = await CurrentApplication.DiscordClient.GetChannelAsync(panel.ChannelId);

            try
            {
                var message = await channel.GetMessageAsync(panel.MessageId);
                await message.ModifyAsync(builder);
            }
            catch (NotFoundException)
            {
                if (!panel.AutoRepost)
                {
                    CurrentApplication.Logger.Warning(
                        "Selfroles: Nachricht {MessageId} ist weg, Neuposten ist aus", panel.MessageId);
                    return false;
                }

                var reposted = await channel.SendMessageAsync(builder);
                await SelfroleService.SetPanelLocationAsync(reposted.ChannelId, reposted.Id);
                CurrentApplication.Logger.Information(
                    "Selfroles: Nachricht war geloescht, neu gepostet als {MessageId}", reposted.Id);
            }

            await SelfroleService.SetRenderedHashAsync(hash);
            return true;
        }
        catch (Exception e)
        {
            CurrentApplication.Logger.Error(e, "Selfroles: Aktualisieren fehlgeschlagen");
            return false;
        }
    }

    #endregion

    #region Member menu

    /// <summary>
    ///     The private menu behind a panel button, built for one member: their roles come back checked,
    ///     so picking one more is one click instead of reselecting everything they already had.
    /// </summary>
    public static DiscordContainerComponent BuildCategoryMenu(SelfroleCategory category,
        IReadOnlySet<string> heldOptionIds, string? notice, bool? autoAssignActive)
    {
        var options = category.ActiveOptions;
        var held = options.Where(option => heldOptionIds.Contains(option.Id)).ToList();

        var children = new List<DiscordComponent>
        {
            new DiscordTextDisplayComponent($"## {category.Name.Truncate(DiscordLimits.EmbedTitle)}")
        };

        if (!string.IsNullOrWhiteSpace(notice))
            children.Add(new DiscordTextDisplayComponent(notice.Truncate(DiscordLimits.EmbedDescription)));

        var description = new StringBuilder();
        description.Append("**Aktuell:** ")
            .Append(held.Count == 0 ? "nichts ausgewählt" : string.Join(", ", held.Select(o => o.Label)));

        if (category.MinValues > 0)
            description.Append("\n-# Aus dieser Kategorie kannst du nur wechseln, nicht alles entfernen.");

        if (autoAssignActive is true)
            description.Append("\n-# 🎮 Autozuweisung ist an: Wenn du ein passendes Spiel spielst, " +
                               "bekommst du die Rolle automatisch.");
        else if (autoAssignActive is false)
            description.Append("\n-# Autozuweisung ist aus. Rollen aus dieser Kategorie bekommst du nur, " +
                               "wenn du sie selbst auswählst.");

        children.Add(new DiscordTextDisplayComponent(description.ToString().Truncate(DiscordLimits.EmbedDescription)));

        var extras = BuildExtraRow(category, held.Count, autoAssignActive);
        var budget = DiscordLimits.ActionRowsPerMessage - (extras is null ? 0 : 1);
        var rows = category.IsSelect
            ? BuildSelectRows(category, options, held, budget)
            : BuildToggleRows(category, options, held, budget);

        if (extras is not null) rows.Add(extras);

        if (rows.Count > 0)
        {
            children.Add(new DiscordSeparatorComponent());
            children.AddRange(rows);
        }

        return new DiscordContainerComponent(children, accentColor: BotConfig.GetEmbedColor());
    }

    private static List<DiscordActionRowComponent> BuildSelectRows(SelfroleCategory category,
        IReadOnlyList<SelfroleOption> options, IReadOnlyCollection<SelfroleOption> held, int budget)
    {
        var rows = new List<DiscordActionRowComponent>();
        var chunks = options.Chunk(DiscordLimits.SelectOptionsPerMenu).Take(budget).ToList();

        for (var index = 0; index < chunks.Count; index++)
        {
            var chunk = chunks[index];
            var selectOptions = chunk.Select(option => new DiscordStringSelectComponentOption(
                option.Label.Truncate(DiscordLimits.SelectOptionLabel),
                option.Id,
                // Discord rejects an empty description, so it has to be absent rather than blank.
                (string.IsNullOrWhiteSpace(option.Description)
                    ? null
                    : option.Description.Truncate(DiscordLimits.SelectOptionDescription))!,
                held.Any(h => h.Id == option.Id),
                ComponentEmojiParser.Parse(option.Emoji)!)).ToList();

            // A minimum per menu would force a pick in every part of a split category, which is not what
            // "at least one" means here, so it only applies while the category fits into one menu.
            var min = chunks.Count == 1 ? Math.Min(category.MinValues, chunk.Length) : 0;
            var max = Math.Min(category.ResolveMaxValues(options.Count), chunk.Length);

            var placeholder = string.IsNullOrWhiteSpace(category.Placeholder) ? category.Name : category.Placeholder;
            if (chunks.Count > 1) placeholder = $"{placeholder} ({index + 1}/{chunks.Count})";

            rows.Add(new DiscordActionRowComponent([
                new DiscordStringSelectComponent(placeholder.Truncate(DiscordLimits.SelectPlaceholder),
                    selectOptions, SelectCustomId(category, index), min, Math.Max(min, max))
            ]));
        }

        return rows;
    }

    private static List<DiscordActionRowComponent> BuildToggleRows(SelfroleCategory category,
        IReadOnlyList<SelfroleOption> options, IReadOnlyCollection<SelfroleOption> held, int budget)
    {
        return
        [
            .. options.Chunk(DiscordLimits.ButtonsPerRow).Take(budget).Select(chunk =>
                new DiscordActionRowComponent([
                    .. chunk.Select(option => new DiscordButtonComponent(
                        held.Any(h => h.Id == option.Id) ? ButtonStyle.Success : ButtonStyle.Secondary,
                        ToggleCustomId(category, option),
                        option.Label.Truncate(DiscordLimits.ButtonLabel),
                        false,
                        ComponentEmojiParser.Parse(option.Emoji)!))
                ]))
        ];
    }

    private static DiscordActionRowComponent? BuildExtraRow(SelfroleCategory category, int heldCount,
        bool? autoAssignActive)
    {
        var buttons = new List<DiscordButtonComponent>();

        if (category.AllowClear && category.MinValues == 0 && heldCount > 0)
            buttons.Add(new DiscordButtonComponent(ButtonStyle.Danger, ClearCustomId(category), "Alle entfernen"));

        if (autoAssignActive is { } active)
            buttons.Add(active
                ? new DiscordButtonComponent(ButtonStyle.Secondary, AutoCustomId(category),
                    "Autozuweisung deaktivieren")
                : new DiscordButtonComponent(ButtonStyle.Success, AutoCustomId(category),
                    "Autozuweisung aktivieren"));

        return buttons.Count == 0 ? null : new DiscordActionRowComponent(buttons);
    }

    /// <summary>The line above the menu after a change, so nobody has to guess what the click did.</summary>
    public static string DescribeChange(SelfroleChange change)
    {
        if (change.Rejection is not null) return change.Rejection;

        var parts = new List<string>();
        if (change.Added.Count > 0) parts.Add("**Hinzugefügt:** " + string.Join(", ", change.Added));
        if (change.Removed.Count > 0) parts.Add("**Entfernt:** " + string.Join(", ", change.Removed));
        if (change.Skipped.Count > 0)
            parts.Add($"**Nicht möglich:** {string.Join(", ", change.Skipped)}. Die Rolle fehlt oder steht über mir.");

        return parts.Count == 0 ? "Nichts geändert." : string.Join("\n", parts);
    }

    #endregion
}
