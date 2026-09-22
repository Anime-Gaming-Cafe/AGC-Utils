#region

using AGC_Management.Entities.Ticket;
using AGC_Management.Services;
using AGC_Management.Utils;

#endregion

namespace AGC_Management.Components;

/// <summary>
///     Builds the support panel and the category picker. Both are driven by the category table and the
///     Tickets section of the runtime settings, so neither needs a deploy to change.
/// </summary>
public class SupportComponents
{
    public const string OpenPanelButtonId = "selectticketcategory";
    public const string CategorySelectId = "ticket_category_select";

    /// <summary>Legacy prefix. Panel messages posted before the picker became a select still send these.</summary>
    public const string LegacyOpenPrefix = "ticket_open_";

    public static async Task<DiscordEmbed> BuildPanelEmbedAsync()
    {
        var title = await RuntimeSettings.GetAsync(TicketCategoryService.SettingsSection, "PanelTitle");
        var description = await RuntimeSettings.GetAsync(TicketCategoryService.SettingsSection, "PanelDescription");
        var footer = await RuntimeSettings.GetAsync(TicketCategoryService.SettingsSection, "PanelFooter");

        var eb = new DiscordEmbedBuilder()
            .WithTitle(string.IsNullOrWhiteSpace(title) ? "AGC Support-System" : title)
            .WithDescription(description ?? "")
            .WithColor(BotConfig.GetEmbedColor());

        if (!string.IsNullOrWhiteSpace(footer)) eb.WithFooter(footer);
        return eb.Build();
    }

    public static DiscordMessageBuilder BuildPanelMessage(DiscordEmbed embed)
    {
        List<DiscordButtonComponent> buttons =
        [
            new DiscordButtonComponent(ButtonStyle.Danger, OpenPanelButtonId, "Ticket öffnen ✉️")
        ];
        return new DiscordMessageBuilder().AddEmbed(embed).AddComponents(buttons);
    }

    /// <summary>
    ///     The ephemeral picker a user gets after pressing "Ticket öffnen". Categories carry their own
    ///     description, so the explanation below the heading is generated rather than written out.
    /// </summary>
    public static async Task<DiscordInteractionResponseBuilder> BuildPickerAsync()
    {
        var categories = await TicketCategoryService.GetAllAsync();
        var title = await RuntimeSettings.GetAsync(TicketCategoryService.SettingsSection, "PickerTitle");
        var description = await RuntimeSettings.GetAsync(TicketCategoryService.SettingsSection, "PickerDescription");

        if (categories.Count == 0)
            return new DiscordInteractionResponseBuilder()
                .AddEmbed(EmbedGenerator.GetErrorEmbed(
                    "Es ist aktuell keine Ticket-Kategorie verfügbar. Bitte wende dich an das Team."))
                .AsEphemeral();

        var body = new System.Text.StringBuilder(description ?? "");
        foreach (var category in categories)
        {
            body.Append("\n\n> ").Append(category.Label);
            if (!string.IsNullOrWhiteSpace(category.Description)) body.Append('\n').Append(category.Description);
        }

        var eb = new DiscordEmbedBuilder()
            .WithTitle(string.IsNullOrWhiteSpace(title) ? "Wähle eine Supportkategorie aus" : title)
            .WithDescription(body.ToString())
            .WithFooter("Wähle bitte die korrekte zu deinem Anliegen zutreffende Kategorie aus!")
            .WithColor(BotConfig.GetEmbedColor());

        var options = categories
            .Take(25)
            .Select(category => new DiscordStringSelectComponentOption(
                category.Label.Truncate(100),
                category.CustomId,
                string.IsNullOrWhiteSpace(category.Description) ? null : category.Description.Truncate(100),
                emoji: BuildEmoji(category)))
            .ToList();

        var selector = new DiscordStringSelectComponent("Wähle eine Kategorie", options, CategorySelectId, 1, 1);
        return new DiscordInteractionResponseBuilder().AddEmbed(eb).AddComponents(selector).AsEphemeral();
    }

    private static DiscordComponentEmoji? BuildEmoji(TicketCategory category)
    {
        if (string.IsNullOrWhiteSpace(category.Emoji)) return null;

        try
        {
            return new DiscordComponentEmoji(category.Emoji);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    ///     Rewrites the panel message that !initsupportpanel put in place. Used by the dashboard so text
    ///     and category changes show up without anyone running the command again.
    /// </summary>
    public static async Task<bool> RefreshPanelAsync()
    {
        try
        {
            var config = BotConfig.GetConfig();
            var channelId = ulong.Parse(config["TicketConfig"]["SupportPanelChannel"]);
            var messageId = ulong.Parse(config["TicketConfig"]["SupportPanelMessage"]);
            if (channelId == 0 || messageId == 0) return false;

            var channel = await CurrentApplication.DiscordClient.GetChannelAsync(channelId);
            var message = await channel.GetMessageAsync(messageId);
            await message.ModifyAsync(BuildPanelMessage(await BuildPanelEmbedAsync()));
            return true;
        }
        catch (Exception e)
        {
            CurrentApplication.Logger.Warning(e, "Could not refresh the support panel message");
            return false;
        }
    }
}
