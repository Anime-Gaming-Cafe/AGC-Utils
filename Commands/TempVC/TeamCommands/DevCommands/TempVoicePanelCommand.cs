#region

using AGC_Management.TempVoice;

#endregion

namespace AGC_Management.Commands.TempVC.TeamCommands.DevCommands;

public sealed class TempVoicePanel : TempVoiceHelper
{
    [Command("initpanel")]
    [RequirePermissions(Permissions.Administrator)]
    public async Task InitVCPanel(CommandContext ctx)
    {
        List<DiscordButtonComponent> buttons = new()
        {
            new DiscordButtonComponent(ButtonStyle.Secondary, "channel_rename",
                emoji: new DiscordComponentEmoji(1085333479732035664)),
            new DiscordButtonComponent(ButtonStyle.Secondary, "channel_limit",
                emoji: new DiscordComponentEmoji(1085333471838343228)),
            new DiscordButtonComponent(ButtonStyle.Secondary, "channel_lock",
                emoji: new DiscordComponentEmoji(1085333475625795605)),
            new DiscordButtonComponent(ButtonStyle.Secondary, "unlock_lock",
                emoji: new DiscordComponentEmoji(1085518424790286346)),
            new DiscordButtonComponent(ButtonStyle.Secondary, "channel_invite",
                emoji: new DiscordComponentEmoji(1085333458840203314)),
            new DiscordButtonComponent(ButtonStyle.Secondary, "channel_delete",
                emoji: new DiscordComponentEmoji(1085333454713004182)),
            new DiscordButtonComponent(ButtonStyle.Secondary, "channel_hide",
                emoji: new DiscordComponentEmoji(1085333456487206973)),
            new DiscordButtonComponent(ButtonStyle.Secondary, "channel_show",
                emoji: new DiscordComponentEmoji(1085333489416671242)),
            new DiscordButtonComponent(ButtonStyle.Secondary, "channel_permit",
                emoji: new DiscordComponentEmoji(1085333477240615094)),
            new DiscordButtonComponent(ButtonStyle.Secondary, "channel_unpermit",
                emoji: new DiscordComponentEmoji(1085333494105919560)),
            new DiscordButtonComponent(ButtonStyle.Secondary, "channel_claim",
                emoji: new DiscordComponentEmoji(1085333451571466301)),
            new DiscordButtonComponent(ButtonStyle.Secondary, "channel_transfer",
                emoji: new DiscordComponentEmoji(1085333484731629578)),
            new DiscordButtonComponent(ButtonStyle.Secondary, "channel_kick",
                emoji: new DiscordComponentEmoji(1085333460366925914)),
            new DiscordButtonComponent(ButtonStyle.Secondary, "channel_ban",
                emoji: new DiscordComponentEmoji(1085333473893556324)),
            new DiscordButtonComponent(ButtonStyle.Secondary, "channel_unban",
                emoji: new DiscordComponentEmoji(1085333487587971102))
        };

        var buttons1 = buttons.Take(5).ToList();
        var buttons2 = buttons.Skip(5).Take(5).ToList();
        var buttons3 = buttons.Skip(10).ToList();

        List<DiscordActionRowComponent> rowComponents = new()
        {
            new DiscordActionRowComponent(buttons1),
            new DiscordActionRowComponent(buttons2),
            new DiscordActionRowComponent(buttons3)
        };

        DiscordEmbedBuilder eb = new()
        {
            Title = $"{BotConfig.GetConfig()["ServerConfig"]["ServerNameInitials"]} Temp-Voice Panel",
            Description =
                $"Hier kannst du die Einstellungen deines Temporären Kanals verändern. \nDu kannst auch Commands in <#{BotConfig.GetConfig()["TempVC"]["CommandChannel_ID"]}> verwenden.",
            Color = BotConfig.GetEmbedColor(),
            ImageUrl = "https://files.fabi-chan.me/resources/agc/tempvoice/%C3%BCbersicht.png"
        };
        var dmb = new DiscordMessageBuilder().AddComponents(rowComponents).AddEmbed(eb.Build());
        var msg = await ctx.Channel.SendMessageAsync(dmb);
        BotConfig.SetConfig("TempVC", "VCPanelMessageID", msg.Id.ToString());
        BotConfig.SetConfig("TempVC", "VCPanelChannelID", ctx.Channel.Id.ToString());
    }
}