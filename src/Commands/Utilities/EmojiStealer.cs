﻿#region

using DisCatSharp.ApplicationCommands;
using DisCatSharp.ApplicationCommands.Attributes;
using DisCatSharp.ApplicationCommands.Context;
using DisCatSharp.Entities;
using DisCatSharp.Enums;
using DisCatSharp.Interactivity.Extensions;

#endregion

namespace AGC_Management.Commands
{
    public class EmojiStealer : ApplicationCommandsModule
    {
        [RequireStaffRole]
        [ContextMenu(ApplicationCommandType.Message, "Steal Emoji")]
        public static async Task StealEmojiMessageCommand(ContextMenuContext ctx)
        {
            DiscordMessage message = ctx.TargetMessage;
            ulong RoleId = ulong.Parse(BotConfig.GetConfig()["ServerConfig"]["StaffRoleId"]);
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
                bool isAnimated = true;
                try
                {
                    string[] splitMessage = message.Content.Split(":");
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
                    if (splitMessage[0].Contains("<a"))
                    {
                        isAnimated = true;
                    }

                    string emojiString = splitMessage[2].Split(">")[0].Trim();

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
                modal.AddTextComponent(new DiscordTextComponent(TextComponentStyle.Small,
                    label: "Neuer Name für den Emoji:", minLength: 2, maxLength: 49));

                await ctx.CreateModalResponseAsync(modal);

                var interactivity = ctx.Client.GetInteractivity();
                var result = await interactivity.WaitForModalAsync(cid, TimeSpan.FromMinutes(2));


                string emojiUrl = $"https://cdn.discordapp.com/emojis/{emoji}.{(isAnimated ? "gif" : "png")}?v=1";
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

                if (result.TimedOut)
                {
                    return;
                }

                var emojiName = result.Result.Interaction.Data.Components[0].Value;


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
            DiscordMessage message = ctx.TargetMessage;
            ulong RoleId = ulong.Parse(BotConfig.GetConfig()["ServerConfig"]["StaffRoleId"]);
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
                modal.AddTextComponent(new DiscordTextComponent(TextComponentStyle.Small,
                    label: "Neuer Name für den Sticker:", minLength: 2, maxLength: 49));
                modal.AddTextComponent(new DiscordTextComponent(TextComponentStyle.Small,
                    label: "Beschreibung für den Sticker:", minLength: 2, maxLength: 100));

                await ctx.CreateModalResponseAsync(modal);

                var interactivity = ctx.Client.GetInteractivity();
                var result = await interactivity.WaitForModalAsync(cid, TimeSpan.FromMinutes(2));
                var stickerurl = message.Stickers[0].Url;
                var stickerdata = message.Stickers[0];
                var stickerBytes = await new HttpClient().GetByteArrayAsync(stickerurl);

                if (result.TimedOut)
                {
                    return;
                }


                var stickerStream = new MemoryStream(stickerBytes);
                var stickerdescription = result.Result.Interaction.Data.Components[1].Value;
                var StickerName = result.Result.Interaction.Data.Components[0].Value;
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
}