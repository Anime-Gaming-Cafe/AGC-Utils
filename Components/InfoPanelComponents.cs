#region

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using AGC_Management.Entities.InfoPanels;
using AGC_Management.Services;
using AGC_Management.Utils;
using DisCatSharp.Exceptions;

#endregion

namespace AGC_Management.Components;

/// <summary>
///     Builds and maintains the panel message. The message is edited in place rather than reposted, and
///     only when something it renders from actually changed, which is what <see cref="ComputeHash" /> is for.
/// </summary>
public static class InfoPanelComponents
{
    public const string Prefix = "ip:";

    private static readonly ConcurrentDictionary<string, Timer> RefreshTimers = new();

    /// <summary>Debounced: saving three fields in a row costs one edit, not three.</summary>
    public static void QueueRefreshPanel(string panelId)
    {
        var timer = RefreshTimers.GetOrAdd(panelId,
            id => new Timer(_ => _ = RefreshPanelAsync(id), null, Timeout.Infinite, Timeout.Infinite));
        timer.Change(2000, Timeout.Infinite);
    }

    public static string SelectCustomId(InfoPanel panel, InfoPanelGroup group)
    {
        return $"{Prefix}{panel.Id}:{group.Id}";
    }

    public static string ButtonCustomId(InfoPanel panel, InfoPanelGroup group, InfoPanelPage page)
    {
        return $"{Prefix}{panel.Id}:{group.Id}:{page.Id}";
    }

    /// <summary>The banner the panel should show right now, or an empty string for none.</summary>
    public static string ResolveBannerUrl(InfoPanel panel)
    {
        return panel.BannerMode switch
        {
            InfoPanel.ModeGuild => CurrentApplication.TargetGuild?.BannerUrl ?? "",
            InfoPanel.ModeUrl => panel.BannerUrl ?? "",
            _ => ""
        };
    }

    public static string ResolveAuthorIconUrl(InfoPanel panel)
    {
        return panel.AuthorIconMode switch
        {
            InfoPanel.ModeGuild => CurrentApplication.TargetGuild?.IconUrl ?? "",
            InfoPanel.ModeUrl => panel.AuthorIconUrl ?? "",
            _ => ""
        };
    }

    /// <summary>
    ///     The header text plus one "Stand der ..." line per group that carries a date label, built from the
    ///     newest page in that group. This replaces the hand-maintained date string of the Python bot.
    /// </summary>
    public static string BuildHeaderDescription(InfoPanel panel)
    {
        var description = new StringBuilder(InfoPanelTemplateResolver.Resolve(panel.HeaderText));

        var dateLines = panel.Groups
            .Where(group => group.Enabled && !string.IsNullOrWhiteSpace(group.DateLabel) &&
                            group.LastUpdatedUnix > 0)
            .OrderBy(group => group.Position)
            .Select(group => $"__{group.DateLabel}:__ <t:{group.LastUpdatedUnix}:D>")
            .ToList();

        if (dateLines.Count > 0)
        {
            if (description.Length > 0) description.Append("\n\n");
            description.Append(string.Join("\n", dateLines));
        }

        return description.ToString().Truncate(DiscordLimits.EmbedDescription);
    }

    public static DiscordMessageBuilder BuildMessage(InfoPanel panel)
    {
        var builder = new DiscordMessageBuilder();
        var color = panel.DiscordColor;

        var bannerUrl = ResolveBannerUrl(panel);
        if (!string.IsNullOrWhiteSpace(bannerUrl))
            builder.AddEmbed(new DiscordEmbedBuilder().WithColor(color).WithImageUrl(bannerUrl));

        var header = new DiscordEmbedBuilder()
            .WithTitle(panel.HeaderTitle.Truncate(DiscordLimits.EmbedTitle))
            .WithDescription(BuildHeaderDescription(panel))
            .WithColor(color);

        if (!string.IsNullOrWhiteSpace(panel.AuthorName))
            header.WithAuthor(panel.AuthorName.Truncate(DiscordLimits.EmbedAuthorName),
                iconUrl: ResolveAuthorIconUrl(panel));

        // Only needed as a width-forcing hack when there's no banner; with a banner, a second
        // embed image in the same message makes Discord shrink both into small gallery tiles.
        if (string.IsNullOrWhiteSpace(bannerUrl) && !string.IsNullOrWhiteSpace(panel.SpacerUrl))
            header.WithImageUrl(panel.SpacerUrl);

        builder.AddEmbed(header);

        var rows = 0;
        foreach (var group in panel.Groups.Where(g => g.Enabled).OrderBy(g => g.Position))
        {
            if (rows >= DiscordLimits.ActionRowsPerMessage) break;

            var pages = group.Pages.Where(page => page.Enabled).OrderBy(page => page.Position).ToList();
            if (pages.Count == 0) continue;

            if (group.IsSelect)
            {
                var options = pages
                    .Take(DiscordLimits.SelectOptionsPerMenu)
                    .Select(page => new DiscordStringSelectComponentOption(
                        page.Label.Truncate(DiscordLimits.SelectOptionLabel),
                        page.Id,
                        string.IsNullOrWhiteSpace(page.Description)
                            ? null
                            : page.Description.Truncate(DiscordLimits.SelectOptionDescription),
                        emoji: BuildEmoji(page.Emoji)))
                    .ToList();

                var placeholder = string.IsNullOrWhiteSpace(group.Placeholder)
                    ? "Auswählen"
                    : group.Placeholder.Truncate(DiscordLimits.SelectPlaceholder);

                builder.AddComponents(new DiscordStringSelectComponent(placeholder, options,
                    SelectCustomId(panel, group), 1, 1));
            }
            else
            {
                var buttons = new List<DiscordComponent>();
                foreach (var page in pages.Take(DiscordLimits.ButtonsPerRow))
                {
                    var label = page.Label.Truncate(DiscordLimits.ButtonLabel);
                    var emoji = BuildEmoji(page.Emoji);

                    if (page.IsLink)
                    {
                        if (string.IsNullOrWhiteSpace(page.Url)) continue;
                        buttons.Add(new DiscordLinkButtonComponent(page.Url, label, false, emoji));
                        continue;
                    }

                    buttons.Add(new DiscordButtonComponent(ResolveButtonStyle(page.ButtonStyle),
                        ButtonCustomId(panel, group, page), label, false, emoji));
                }

                if (buttons.Count == 0) continue;
                builder.AddComponents(buttons);
            }

            rows++;
        }

        return builder;
    }

    private static ButtonStyle ResolveButtonStyle(int style)
    {
        return Enum.IsDefined(typeof(ButtonStyle), style) && style != (int)ButtonStyle.Link
            ? (ButtonStyle)style
            : ButtonStyle.Primary;
    }

    private static DiscordComponentEmoji? BuildEmoji(string? emoji)
    {
        if (string.IsNullOrWhiteSpace(emoji)) return null;

        try
        {
            return new DiscordComponentEmoji(emoji);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    ///     Everything the message is built from, hashed. The resolved banner and icon urls are included
    ///     because they carry Discord's asset hash, so a new server banner changes this value. The header
    ///     goes in rendered rather than raw, so a placeholder such as {membercount} keeps the message
    ///     moving instead of freezing at whatever the count was when somebody last saved.
    /// </summary>
    public static string ComputeHash(InfoPanel panel)
    {
        var sb = new StringBuilder();
        sb.Append(panel.HeaderTitle).Append('\u001f')
            .Append(BuildHeaderDescription(panel)).Append('\u001f')
            .Append(panel.AuthorName).Append('\u001f')
            .Append(ResolveAuthorIconUrl(panel)).Append('\u001f')
            .Append(ResolveBannerUrl(panel)).Append('\u001f')
            .Append(panel.SpacerUrl).Append('\u001f')
            .Append(panel.Color).Append('\u001e');

        foreach (var group in panel.Groups.Where(g => g.Enabled).OrderBy(g => g.Position))
        {
            sb.Append(group.Id).Append('\u001f')
                .Append(group.Kind).Append('\u001f')
                .Append(group.Placeholder).Append('\u001f')
                .Append(group.DateLabel).Append('\u001f')
                .Append(group.LastUpdatedUnix).Append('\u001e');

            foreach (var page in group.Pages.Where(p => p.Enabled).OrderBy(p => p.Position))
                sb.Append(page.Id).Append('\u001f')
                    .Append(page.Kind).Append('\u001f')
                    .Append(page.Label).Append('\u001f')
                    .Append(page.Description).Append('\u001f')
                    .Append(page.Emoji).Append('\u001f')
                    .Append(page.ButtonStyle).Append('\u001f')
                    .Append(page.Url).Append('\u001e');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    /// <summary>Posts the panel into a channel and remembers where it went. Used by the dashboard.</summary>
    public static async Task SendPanelToChannelAsync(string panelId, ulong channelId)
    {
        var panel = await InfoPanelService.GetAsync(panelId);
        if (panel is null) throw new InvalidOperationException($"Es gibt kein Panel mit der ID {panelId}.");

        var channel = await CurrentApplication.DiscordClient.GetChannelAsync(channelId);
        var message = await channel.SendMessageAsync(BuildMessage(panel));

        await InfoPanelService.SetLocationAsync(panelId, message.ChannelId, message.Id);
        await InfoPanelService.SetRenderedHashAsync(panelId, ComputeHash(panel));
        await InfoPanelService.SetEnabledAsync(panelId, true);
    }

    /// <summary>
    ///     Brings the posted message back in line with the database. Does nothing when the rendered result
    ///     is unchanged, so the periodic task costs no API calls on a quiet server. Returns false when
    ///     there was nothing to refresh.
    /// </summary>
    public static async Task<bool> RefreshPanelAsync(string panelId, bool force = false)
    {
        try
        {
            InfoPanelService.Invalidate();
            var panel = await InfoPanelService.GetAsync(panelId);
            if (panel is null || !panel.IsPosted) return false;

            var hash = ComputeHash(panel);
            if (!force && hash == panel.RenderedHash) return true;

            var builder = BuildMessage(panel);
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
                        "InfoPanel {PanelId}: Nachricht {MessageId} ist weg, Neuposten ist aus", panelId,
                        panel.MessageId);
                    return false;
                }

                var reposted = await channel.SendMessageAsync(builder);
                await InfoPanelService.SetLocationAsync(panelId, reposted.ChannelId, reposted.Id);
                CurrentApplication.Logger.Information(
                    "InfoPanel {PanelId}: Nachricht war geloescht, neu gepostet als {MessageId}", panelId,
                    reposted.Id);
            }

            await InfoPanelService.SetRenderedHashAsync(panelId, hash);
            return true;
        }
        catch (Exception e)
        {
            CurrentApplication.Logger.Error(e, "InfoPanel {PanelId}: Aktualisieren fehlgeschlagen", panelId);
            return false;
        }
    }

    /// <summary>Refreshes every posted panel whose banner or author icon follows the guild.</summary>
    public static async Task RefreshGuildAssetPanelsAsync()
    {
        foreach (var panel in await InfoPanelService.GetAllAsync())
        {
            if (!panel.IsPosted) continue;
            if (panel.BannerMode != InfoPanel.ModeGuild && panel.AuthorIconMode != InfoPanel.ModeGuild) continue;

            QueueRefreshPanel(panel.Id);
        }
    }
}
