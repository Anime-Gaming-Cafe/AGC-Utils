#region

using System.Text;
using AGC_Management.Entities.ApplicationSystem;
using AGC_Management.Enums;
using AGC_Management.Services;
using AGC_Management.Utils;

#endregion

namespace AGC_Management.ApplicationSystem;

public sealed class ApplyPanelCommands : BaseCommandModule
{
    public const string SelectorId = "applypanelselector";
    public const string MyApplicationsId = "applypanel_myapplications";

    /// <summary>Discord caps a string select at 25 options.</summary>
    private const int MaxSelectOptions = 25;

    // Static so a refresh queued from a Razor page works before CommandsNext ever built an instance.
    private static readonly Timer RefreshTimer =
        new(_ => _ = RefreshPanel(), null, Timeout.Infinite, Timeout.Infinite);

    /// <summary>Debounced: every call pushes the refresh two seconds out instead of queueing another one.</summary>
    public static void QueueRefreshPanel()
    {
        RefreshTimer.Change(2000, Timeout.Infinite);
    }

    [RequirePermissions(Permissions.Administrator)]
    [Command("sendapplypanel")]
    [Description("Sends the apply panel to the channel.")]
    public async Task SendPanel(CommandContext ctx)
    {
        try
        {
            await ctx.Message.DeleteAsync();
        }
        catch (Exception e)
        {
            CurrentApplication.Logger.Error(e, "Failed to trigger delete message");
        }

        var msgb = await BuildMessage();
        var m = await ctx.Channel.SendMessageAsync(msgb);
        await StorePanelLocationAsync(m.ChannelId, m.Id);
    }

    /// <summary>Sends the panel to a specific channel (used by the WebUI) and stores its location.</summary>
    public static async Task SendPanelToChannelAsync(ulong channelId)
    {
        var channel = await CurrentApplication.DiscordClient.GetChannelAsync(channelId);
        var msgb = await BuildMessage();
        var m = await channel.SendMessageAsync(msgb);
        await StorePanelLocationAsync(m.ChannelId, m.Id);
    }

    private static async Task StorePanelLocationAsync(ulong channelId, ulong messageId)
    {
        await CachingService.SetCacheValue(CustomDatabaseCacheType.ApplicationSystemCache, "applymessageid",
            messageId.ToString());
        await CachingService.SetCacheValue(CustomDatabaseCacheType.ApplicationSystemCache, "applychannelid",
            channelId.ToString());
        await CachingService.SetCacheValue(CustomDatabaseCacheType.ApplicationSystemCache, "ispanelactive", "true");
    }

    /// <summary>Where the panel currently lives, or (0, 0) when none was ever sent.</summary>
    public static async Task<(ulong channelId, ulong messageId)> GetPanelLocationAsync()
    {
        var rawChannel = await CachingService.GetCacheValue(CustomDatabaseCacheType.ApplicationSystemCache,
            "applychannelid");
        var rawMessage = await CachingService.GetCacheValue(CustomDatabaseCacheType.ApplicationSystemCache,
            "applymessageid");

        ulong.TryParse(rawChannel, out var channelId);
        ulong.TryParse(rawMessage, out var messageId);
        return (channelId, messageId);
    }

    public static async Task RefreshPanel()
    {
        var m_id = await CachingService.GetCacheValue(CustomDatabaseCacheType.ApplicationSystemCache, "applymessageid");
        var c_id = await CachingService.GetCacheValue(CustomDatabaseCacheType.ApplicationSystemCache, "applychannelid");
        if (string.IsNullOrEmpty(m_id) || m_id == "0" || string.IsNullOrEmpty(c_id) || c_id == "0") return;

        try
        {
            var msgb = await BuildMessage();
            var channel = await CurrentApplication.DiscordClient.GetChannelAsync(ulong.Parse(c_id));
            var m = await channel.GetMessageAsync(ulong.Parse(m_id));
            await m.ModifyAsync(msgb);
        }
        catch (Exception e)
        {
            CurrentApplication.Logger.Error(e, "Failed to refresh apply panel");
        }
    }

    /// <summary>
    ///     A position without an open phase stays visible and selectable; the listener answers the click with
    ///     the closed notice so the applicant learns why nothing happens.
    /// </summary>
    private static async Task<DiscordMessageBuilder> BuildMessage()
    {
        var positions = await TeamApplicationService.GetPositionsAsync(true);
        var closedText = await TeamApplicationService.GetTextAsync("PanelClosedText",
            "Bewerbungen aktuell geschlossen");
        var opensAtText = await TeamApplicationService.GetTextAsync("PanelOpensAtText",
            "Naechste Bewerbungsphase ab");

        var options = new List<DiscordStringSelectComponentOption>();
        foreach (var position in positions.Take(MaxSelectOptions))
        {
            var opening = await TeamApplicationService.ResolveOpeningAsync(position);
            string description;

            if (opening.CanApply)
            {
                description = "✅ Diese Position ist bewerbbar";
            }
            else
            {
                description = $"❌ {closedText}";
                if (opening.NextOpensAt > 0)
                    description +=
                        $" | {opensAtText} {ToolSet.GetFormattedTimeFromUnixAndRespectTimeZone(opening.NextOpensAt)}";
            }

            options.Add(new DiscordStringSelectComponentOption(position.PositionName, position.PositionId,
                description.Truncate(100)));
        }

        var panelText =
            await CachingService.GetCacheValueAsBase64(CustomDatabaseCacheType.ApplicationSystemCache,
                "applypaneltext");
        var panelDescription = new StringBuilder(string.IsNullOrEmpty(panelText)
            ? "⚠️ Es wurde noch kein Text für das Bewerbungspanel festgelegt. ⚠️"
            : panelText);

        if (options.Count == 0) panelDescription.Append("\n\nEs sind aktuell keine Bewerbungspositionen verfügbar.");

        var embed = new DiscordEmbedBuilder()
            .WithTitle("Bewerbung")
            .WithDescription(panelDescription.ToString())
            .WithColor(DiscordColor.Gold)
            .WithFooter("AGC Bewerbungssystem", CurrentApplication.TargetGuild?.IconUrl ?? "");

        var msgb = new DiscordMessageBuilder().AddEmbed(embed);

        if (options.Count > 0)
            msgb.AddComponents(new DiscordStringSelectComponent("Wähle die gewünschte Bewerbungsposition aus",
                options, SelectorId));

        var applyNowButton = new DiscordLinkButtonComponent(ToolSet.GetDashboardUrl("apply"), "Jetzt bewerben");
        var myApplicationsButton = new DiscordButtonComponent(ButtonStyle.Secondary, MyApplicationsId,
            "Meine Bewerbungen");
        msgb.AddComponents(applyNowButton, myApplicationsButton);

        return msgb;
    }
}
