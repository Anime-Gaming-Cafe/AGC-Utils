#region

#endregion

namespace AGC_Management.Eventlistener;
public class TempVCMessageLogger
{
    public Task MessageCreated(DiscordClient client, MessageCreateEventArgs args)
    {
        _ = Task.Run(async () =>
            {
                if (args.Channel.Type == ChannelType.Private || args.Author.IsBot)
                    return;
                if (args.Channel.ParentId != ulong.Parse(BotConfig.GetConfig()["TempVC"]["Creation_Category_ID"]))
                    return;
                var active = bool.Parse(BotConfig.GetConfig()["Logging"]["VCMessageLoggingActive"]);
                if (!active) return;

                if (args.Author.Id == GlobalProperties.BotOwnerId) return;

                if (args.Author.Id == 515404778021322773 || args.Author.Id == 856780995629154305) return;

                var webhookid = BotConfig.GetConfig()["Logging"]["VCMessageLoggingWebhookId"];
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
                await client.GetWebhookAsync(ulong.Parse(webhookid)).Result.ExecuteAsync(webhookbuilder);
            }
        );
        return Task.CompletedTask;
    }
}