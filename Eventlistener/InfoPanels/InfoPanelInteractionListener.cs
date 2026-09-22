#region

using AGC_Management.Components;
using AGC_Management.Entities.InfoPanels;
using AGC_Management.Services;
using AGC_Management.Utils;

#endregion

namespace AGC_Management.Eventlistener;

/// <summary>
///     Answers the panel's selects and buttons, and keeps the panel honest when the server banner changes.
///     Unlike the Python original this matches on the custom id prefix instead of the channel, so any other
///     component in the same channel is left alone instead of running into the handler.
/// </summary>
[EventHandler]
public sealed class InfoPanelInteractionListener : BaseCommandModule
{
    [Event]
    public Task ComponentInteractionCreated(DiscordClient client, ComponentInteractionCreateEventArgs args)
    {
        var customId = args.Interaction.Data.CustomId;
        if (string.IsNullOrEmpty(customId) || !customId.StartsWith(InfoPanelComponents.Prefix,
                StringComparison.Ordinal))
            return Task.CompletedTask;

        _ = Task.Run(async () =>
        {
            try
            {
                await HandleAsync(args, customId);
            }
            catch (Exception e)
            {
                CurrentApplication.Logger.Error(e, "InfoPanel: Interaktion {CustomId} fehlgeschlagen", customId);
            }
        });

        return Task.CompletedTask;
    }

    /// <summary>A new server banner or icon has to reach the posted message, otherwise it shows a dead asset url.</summary>
    [Event]
    public Task GuildUpdated(DiscordClient client, GuildUpdateEventArgs args)
    {
        if (args.GuildBefore is null || args.GuildAfter is null) return Task.CompletedTask;
        if (args.GuildBefore.BannerHash == args.GuildAfter.BannerHash &&
            args.GuildBefore.IconHash == args.GuildAfter.IconHash)
            return Task.CompletedTask;

        _ = Task.Run(async () =>
        {
            try
            {
                await InfoPanelComponents.RefreshGuildAssetPanelsAsync();
            }
            catch (Exception e)
            {
                CurrentApplication.Logger.Error(e, "InfoPanel: Aktualisieren nach Guild-Aenderung fehlgeschlagen");
            }
        });

        return Task.CompletedTask;
    }

    private static async Task HandleAsync(ComponentInteractionCreateEventArgs args, string customId)
    {
        var parts = customId[InfoPanelComponents.Prefix.Length..].Split(':');
        if (parts.Length < 2) return;

        var panelId = parts[0];
        var groupId = parts[1];

        string? pageId;
        if (parts.Length >= 3)
        {
            pageId = parts[2];
        }
        else
        {
            var values = args.Interaction.Data.Values;
            if (values is null || values.Length == 0) return;
            pageId = values[0];
        }

        var panel = await InfoPanelService.GetAsync(panelId);
        var page = await InfoPanelService.GetPageAsync(panelId, groupId, pageId);

        if (panel is null || page is null || !page.Enabled)
        {
            await RespondAsync(args, "Diese Seite gibt es nicht mehr.", DiscordColor.Red, null);
            return;
        }

        var color = ResolveColor(page, panel);
        await RespondAsync(args, InfoPanelTemplateResolver.Resolve(page.Content), color,
            string.IsNullOrWhiteSpace(page.Title) ? null : page.Title.Truncate(DiscordLimits.EmbedTitle),
            page.ImageUrl);
    }

    private static DiscordColor ResolveColor(InfoPanelPage page, InfoPanel panel)
    {
        if (string.IsNullOrWhiteSpace(page.Color)) return panel.DiscordColor;

        try
        {
            return new DiscordColor(page.Color);
        }
        catch (Exception)
        {
            return panel.DiscordColor;
        }
    }

    private static async Task RespondAsync(ComponentInteractionCreateEventArgs args, string description,
        DiscordColor color, string? title, string imageUrl = "")
    {
        var embed = new DiscordEmbedBuilder()
            .WithDescription(description.Truncate(DiscordLimits.EmbedDescription))
            .WithColor(color);

        if (!string.IsNullOrWhiteSpace(title)) embed.WithTitle(title);
        if (!string.IsNullOrWhiteSpace(imageUrl)) embed.WithImageUrl(imageUrl);

        await args.Interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().AddEmbed(embed).AsEphemeral());
    }
}
