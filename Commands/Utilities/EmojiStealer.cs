#region

using AGC_Management.Attributes;

using DisCatSharp.ApplicationCommands;
using DisCatSharp.ApplicationCommands.Attributes;
using DisCatSharp.ApplicationCommands.Context;
using DisCatSharp.Interactivity;
using DisCatSharp.Interactivity.Extensions;

#endregion

namespace AGC_Management.Commands;

public class EmojiStealer : ApplicationCommandsModule
{
    [RequireStaffRole]
    [ContextMenu(ApplicationCommandType.Message, "Steal Emoji")]
    public static async Task StealEmojiMessageCommand(ContextMenuContext ctx)
    {
        var message = ctx.TargetMessage;
        var RoleId = ulong.Parse(BotConfig.GetConfig()["ServerConfig"]["StaffRoleId"]);
        if (!ctx.Member.Roles.Any(r => r.Id == RoleId))
        {
            var ib = new DiscordInteractionResponseBuilder();
            ib.IsEphemeral = true;
            ib.WithContent("Du hast nicht die benötigten Rechte um diesen Command auszuführen!");
            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, ib);
            return;
        }

        if (message.Content == "")
        {
            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().WithContent("Diese Nachricht hat keinen Inhalt!")
                    .AsEphemeral());
            return;
        }

        {
            string emoji;
            var isAnimated = true;
            try
            {
                var splitMessage = message.Content.Split(":");
                if (splitMessage.Length < 3)
                {
                    await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                        new DiscordInteractionResponseBuilder().WithContent("Diese Nachricht enthält kein Emoji!")
                            .AsEphemeral());
                    return;
                }

                emoji = splitMessage[0];
                Console.WriteLine(emoji);
                isAnimated = false;
                if (splitMessage[0].Contains("<a")) isAnimated = true;

                var emojiString = splitMessage[2].Split(">")[0].Trim();

                emoji = emojiString;
            }
            catch
            {
                await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                    new DiscordInteractionResponseBuilder().WithContent("Ein Fehler ist aufgetreten!")
                        .AsEphemeral());

                return;
            }

            var randomid = new Random();
            var cid = randomid.Next(100000, 999999).ToString();
            DiscordInteractionModalBuilder modal = new();
            modal.WithTitle("Emoji Stealer");
            modal.CustomId = cid;
            modal.AddLabelComponent(new("Emoji name", component: new DiscordTextInputComponent(TextComponentStyle.Small, minLength: 2, maxLength: 49)));
			await ctx.CreateModalResponseAsync(modal);

            var interactivity = ctx.Client.GetInteractivity();
            var result = await interactivity.WaitForModalAsync(cid, TimeSpan.FromMinutes(2));


            var emojiUrl = $"https://cdn.discordapp.com/emojis/{emoji}.{(isAnimated ? "gif" : "png")}?v=1";
            var emojiBytes = await new HttpClient().GetByteArrayAsync(emojiUrl);
            var emojiStream = new MemoryStream(emojiBytes);

            if (emoji == null)
            {
                await result.Result.Interaction.CreateResponseAsync(
                    InteractionResponseType.DeferredChannelMessageWithSource,
                    new DiscordInteractionResponseBuilder().AsEphemeral());
                await result.Result.Interaction.EditOriginalResponseAsync(
                    new DiscordWebhookBuilder().WithContent("Diese Nachricht enthält kein Emoji"));
                return;
            }

            if (result.TimedOut) return;

            var emojiName = (result.Result.Interaction.Data.ModalComponents.OfType<DiscordLabelComponent>().First().Component as DiscordTextInputComponent)!.Value;

            await result.Result.Interaction.CreateResponseAsync(
                InteractionResponseType.DeferredChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().AsEphemeral());
            try
            {
                await ctx.Guild.CreateEmojiAsync(emojiName, emojiStream);
                await result.Result.Interaction.EditOriginalResponseAsync(
                    new DiscordWebhookBuilder().WithContent(
                        $"Emoji ``{emojiName}`` wurde erfolgreich hinzugefügt!"));
            }
            catch (Exception e)
            {
                await result.Result.Interaction.EditOriginalResponseAsync(
                    new DiscordWebhookBuilder().WithContent(
                        $"Fehler beim hinzufügen des Emojis: ```{e.Message}```"));
            }
        }
    }

    [RequireStaffRole]
    [ContextMenu(ApplicationCommandType.Message, "Steal Sticker")]
    public static async Task StealStickerMessageCommand(ContextMenuContext ctx)
    {
        var message = ctx.TargetMessage;
        var RoleId = ulong.Parse(BotConfig.GetConfig()["ServerConfig"]["StaffRoleId"]);
        if (!ctx.Member.Roles.Any(r => r.Id == RoleId))
        {
            var ib = new DiscordInteractionResponseBuilder();
            ib.IsEphemeral = true;
            ib.WithContent("Du hast nicht die benötigten Rechte um diesen Command auszuführen!");
            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, ib);
            return;
        }

        if (message.Stickers.Count == 0)
        {
            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().WithContent("Diese Nachricht hat keinen Sticker!")
                    .AsEphemeral());
            return;
        }

        {
            var randomid = new Random();
            var cid = randomid.Next(100000, 999999).ToString();
            DiscordInteractionModalBuilder modal = new();
            modal.WithTitle("Sticker Stealer");
            modal.CustomId = cid;
            modal.AddLabelComponent(new("Neuer Name", component: new DiscordTextInputComponent(TextComponentStyle.Small, minLength: 2, maxLength: 49)));
            modal.AddLabelComponent(new("Beschreibung", component: new DiscordTextInputComponent(TextComponentStyle.Small, minLength: 2, maxLength: 100)));

            await ctx.CreateModalResponseAsync(modal);

            var interactivity = ctx.Client.GetInteractivity();
            var result = await interactivity.WaitForModalAsync(cid, TimeSpan.FromMinutes(2));
            var stickerurl = message.Stickers[0].Url;
            var stickerdata = message.Stickers[0];
            var stickerBytes = await new HttpClient().GetByteArrayAsync(stickerurl);

            if (result.TimedOut) return;


            var stickerStream = new MemoryStream(stickerBytes);
            var stickerdescription = (result.Result.Interaction.Data.ModalComponents.OfType<DiscordLabelComponent>().ElementAt(1).Component as DiscordTextInputComponent)!.Value;
            var StickerName = (result.Result.Interaction.Data.ModalComponents.OfType<DiscordLabelComponent>().First().Component as DiscordTextInputComponent)!.Value;
            await result.Result.Interaction.CreateResponseAsync(
                InteractionResponseType.DeferredChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().AsEphemeral());
            try
            {
                await ctx.Guild.CreateStickerAsync(StickerName, stickerdescription,
                    DiscordEmoji.FromName(ctx.Client, ":robot:"), stickerStream, stickerdata.FormatType);
                await result.Result.Interaction.EditOriginalResponseAsync(
                    new DiscordWebhookBuilder().WithContent(
                        $"Sticker ``{StickerName}`` wurde erfolgreich hinzugefügt!"));
            }
            catch (Exception e)
            {
                await result.Result.Interaction.EditOriginalResponseAsync(
                    new DiscordWebhookBuilder().WithContent(
                        $"Fehler beim hinzufügen des Stickers: ```{e.Message}```"));
            }
        }
    }
}