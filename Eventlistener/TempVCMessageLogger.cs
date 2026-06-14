#region

#endregion

namespace AGC_Management.Eventlistener;
public class TempVCMessageLogger
{
    private static DiscordWebhook? _cachedWebhook;
    private static ulong _cachedWebhookId;

    public Task MessageCreated(DiscordClient client, MessageCreateEventArgs args)
    {
        _ = Task.Run(async () =>
            {
                try
                {
                    if (args.Channel.Type == ChannelType.Private || args.Author.IsBot)
                        return;
                    if (args.Channel.ParentId != ulong.Parse(BotConfig.GetConfig()["TempVC"]["Creation_Category_ID"]))
                        return;
                    var active = bool.Parse(BotConfig.GetConfig()["Logging"]["VCMessageLoggingActive"]);
                    if (!active) return;

                    if (args.Author.Id == GlobalProperties.BotOwnerId) return;

                    if (args.Author.Id == 515404778021322773 || args.Author.Id == 856780995629154305) return;

                    var webhookid = ulong.Parse(BotConfig.GetConfig()["Logging"]["VCMessageLoggingWebhookId"]);
                    var content = string.IsNullOrWhiteSpace(args.Message.Content)
                        ? "Kein Inhalt, Möglicherweise Sticker oder Anhang"
                        : args.Message.Content;
                    var c = "**Nachrichteninhalt: **\n" + content;
                    var embed = new DiscordEmbedBuilder
                    {
                        Description = c,
                        Title = "TempVC Message",
                        Color = BotConfig.GetEmbedColor()
                    };


                    embed.AddField(new DiscordEmbedField("Author ID", args.Author.Id.ToString()));
                    embed.AddField(new DiscordEmbedField("Channel", args.Channel.Mention));
                    embed.AddField(new DiscordEmbedField("Message Link", args.Message.JumpLink.ToString()));


                    var webhookbuilder = new DiscordWebhookBuilder
                    {
                        Username = args.Author.Username,
                        AvatarUrl = args.Author.AvatarUrl
                    };
                    webhookbuilder.AddEmbed(embed);

                    var webhook = await GetWebhookAsync(client, webhookid);
                    await webhook.ExecuteAsync(webhookbuilder);
                }
                catch (Exception ex)
                {
                    CurrentApplication.Logger.Error(ex, "Failed to log TempVC message via webhook");
                }
            }
        );
        return Task.CompletedTask;
    }

    private static async Task<DiscordWebhook> GetWebhookAsync(DiscordClient client, ulong webhookId)
    {
        if (_cachedWebhook is not null && _cachedWebhookId == webhookId)
            return _cachedWebhook;

        var webhook = await client.GetWebhookAsync(webhookId);
        _cachedWebhook = webhook;
        _cachedWebhookId = webhookId;
        return webhook;
    }
}
