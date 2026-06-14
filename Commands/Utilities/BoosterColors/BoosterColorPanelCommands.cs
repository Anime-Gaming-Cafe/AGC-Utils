#region

using AGC_Management.Services;

#endregion

namespace AGC_Management.BoosterColors;

public sealed class BoosterColorPanelCommands : BaseCommandModule
{
    private static readonly Queue<Task> refreshQueue = new();
    private static Timer timer;

    public BoosterColorPanelCommands()
    {
        timer = new Timer(RefreshPanelFromQueue, null, Timeout.Infinite, Timeout.Infinite);
    }

    public static void QueueRefreshPanel()
    {
        refreshQueue.Clear();
        refreshQueue.Enqueue(new Task(async () => await RefreshPanel()));
        timer.Change(2000, Timeout.Infinite);
    }

    private static void RefreshPanelFromQueue(object state)
    {
        if (refreshQueue.Count > 0)
        {
            var task = refreshQueue.Dequeue();
            task.Start();

            if (refreshQueue.Count > 0)
                timer.Change(5000, Timeout.Infinite);
            else
                timer.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    [RequirePermissions(Permissions.Administrator)]
    [Command("sendboostercolorpanel")]
    [Description("Sendet das Booster Farben Panel in diesen Channel.")]
    public async Task SendPanel(CommandContext ctx)
    {
        try
        {
            await ctx.Message.DeleteAsync();
        }
        catch (Exception e)
        {
            CurrentApplication.Logger.Error(e, "Failed to delete command message");
        }

        var msgb = await BuildMessageAsync();
        var m = await ctx.Channel.SendMessageAsync(msgb);
        await BoosterColorService.SetPanelLocationAsync(m.ChannelId, m.Id);
        await BoosterColorService.SetEnabledAsync(true);
    }

    /// <summary>
    ///     Sends the panel to a specific channel (used by the WebUI) and stores its location.
    /// </summary>
    public static async Task SendPanelToChannelAsync(ulong channelId)
    {
        var channel = await CurrentApplication.DiscordClient.GetChannelAsync(channelId);
        var msgb = await BuildMessageAsync();
        var m = await channel.SendMessageAsync(msgb);
        await BoosterColorService.SetPanelLocationAsync(m.ChannelId, m.Id);
        await BoosterColorService.SetEnabledAsync(true);
    }

    public static async Task RefreshPanel()
    {
        var channelId = await BoosterColorService.GetPanelChannelIdAsync();
        var messageId = await BoosterColorService.GetPanelMessageIdAsync();
        if (channelId == 0 || messageId == 0) return;

        var msgb = await BuildMessageAsync();
        try
        {
            var channel = await CurrentApplication.DiscordClient.GetChannelAsync(channelId);
            var m = await channel.GetMessageAsync(messageId);
            await m.ModifyAsync(msgb);
        }
        catch (Exception e)
        {
            CurrentApplication.Logger.Error(e, "Failed to refresh booster color panel");
        }
    }

    public static async Task<DiscordMessageBuilder> BuildMessageAsync()
    {
        var guild = CurrentApplication.TargetGuild;
        var title = await BoosterColorService.GetEmbedTitleAsync();
        var description = await BoosterColorService.GetEmbedDescriptionAsync();

        var emb = new DiscordEmbedBuilder()
            .WithTitle(title)
            .WithColor(BotConfig.GetEmbedColor())
            .WithFooter("AGC Booster Farben", guild.IconUrl);

        var msgb = new DiscordMessageBuilder();

        var boundaries = BoosterColorService.GetBoundaries(guild);
        if (boundaries == null)
        {
            emb.WithDescription(description +
                                "\n\n⚠️ Die Boundary-Rollen `BEGIN_BOOSTERCOLOR` / `END_BOOSTERCOLOR` wurden nicht gefunden.");
            return msgb.AddEmbed(emb);
        }

        var colorRoles = BoosterColorService.GetColorRoles(guild);
        var emojiLookup = await BoosterColorService.GetEmojiLookupAsync();

        var options = new List<DiscordStringSelectComponentOption>();
        // Discord allows max 25 options. Reserve one for the reset option.
        foreach (var role in colorRoles.Take(24))
            options.Add(new DiscordStringSelectComponentOption(
                role.Name,
                role.Id.ToString(),
                emoji: BoosterColorService.GetComponentEmoji(role, emojiLookup)));

        options.Add(new DiscordStringSelectComponentOption(
            "Levelfarbe (Zurücksetzen)",
            BoosterColorService.ResetValue,
            "Entfernt deine aktuelle Booster Farbe",
            emoji: new DiscordComponentEmoji("❌")));

        emb.WithDescription(colorRoles.Count == 0
            ? description + "\n\nEs sind aktuell keine Farben verfügbar."
            : description);

        var selector = new DiscordStringSelectComponent(
            "Wähle deine Booster Farbe", options, BoosterColorService.SelectorCustomId);

        msgb.AddEmbed(emb);
        msgb.AddComponents(selector);

        return msgb;
    }
}
