#region

using System.Text.RegularExpressions;
using AGC_Management.Services;
using DisCatSharp.Exceptions;

#endregion

namespace AGC_Management.Eventlistener;

[EventHandler]
public class AutoQuote : BaseCommandModule
{
    private const int MaxQuotesPerMessage = 3;

    private static readonly Regex MessageLinkRegex = new(
        @"(?:https?://)?(?:\w+\.)?discord(?:app)?\.com/channels/(\d+)/(\d+)/(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    [Event]
    public static Task MessageCreated(DiscordClient client, MessageCreateEventArgs args)
    {
        if (args.Author.IsBot || args.Guild is null) return Task.CompletedTask;
        if (args.Guild.Id != CurrentApplication.TargetGuild?.Id) return Task.CompletedTask;

        var content = args.Message.Content;
        if (string.IsNullOrEmpty(content) || content.StartsWith('!')) return Task.CompletedTask;
        if (!MessageLinkRegex.IsMatch(content)) return Task.CompletedTask;

        _ = Task.Run(async () =>
        {
            try
            {
                await QuoteLinksAsync(client, args, args.Guild);
            }
            catch (Exception e)
            {
                CurrentApplication.Logger.Warning(e, "Auto quote failed for message {MessageId}", args.Message.Id);
            }
        });

        return Task.CompletedTask;
    }

    private static async Task QuoteLinksAsync(DiscordClient client, MessageCreateEventArgs args, DiscordGuild guild)
    {
        if (!await AutoQuoteService.GetEnabledAsync()) return;
        if (AutoQuoteService.IsExcluded(args.Channel, await AutoQuoteService.GetExcludedListenAsync())) return;

        var excludedSources = await AutoQuoteService.GetExcludedSourcesAsync();
        var links = MessageLinkRegex.Matches(args.Message.Content)
            .Where(m => ulong.TryParse(m.Groups[1].Value, out var guildId) && guildId == guild.Id)
            .Select(m => (Channel: ulong.TryParse(m.Groups[2].Value, out var c) ? c : 0,
                Message: ulong.TryParse(m.Groups[3].Value, out var id) ? id : 0))
            .Where(l => l.Channel != 0 && l.Message != 0)
            .DistinctBy(l => l.Message)
            .Take(MaxQuotesPerMessage);

        foreach (var link in links)
        {
            var channel = await ResolveChannelAsync(client, guild, link.Channel);
            if (channel is null || AutoQuoteService.IsExcluded(channel, excludedSources)) continue;

            DiscordMessage quoted;
            try
            {
                quoted = await channel.GetMessageAsync(link.Message);
            }
            catch (Exception e) when (e is NotFoundException or UnauthorizedException)
            {
                continue;
            }

            var reply = new DiscordMessageBuilder()
                .AddEmbed(BuildQuoteEmbed(quoted, channel))
                .AddComponents(new DiscordLinkButtonComponent(quoted.JumpLink.ToString(), "Zur Nachricht"))
                .WithReply(args.Message.Id);
            await args.Channel.SendMessageAsync(reply);
        }
    }

    private static async Task<DiscordChannel?> ResolveChannelAsync(DiscordClient client, DiscordGuild guild,
        ulong channelId)
    {
        var channel = guild.GetChannel(channelId);
        if (channel is not null) return channel;
        if (guild.Threads.TryGetValue(channelId, out var thread)) return thread;

        try
        {
            channel = await client.GetChannelAsync(channelId);
            return channel.GuildId == guild.Id ? channel : null;
        }
        catch (Exception e) when (e is NotFoundException or UnauthorizedException)
        {
            return null;
        }
    }

    private static DiscordEmbed BuildQuoteEmbed(DiscordMessage quoted, DiscordChannel channel)
    {
        var footer = $"Zitat aus #{channel.Name}";
        var embed = new DiscordEmbedBuilder()
            .WithAuthor($"Gesendet von {quoted.Author.Username}", iconUrl: quoted.Author.AvatarUrl)
            .WithTimestamp(quoted.Timestamp)
            .WithColor(BotConfig.GetEmbedColor());

        var firstImage = quoted.Attachments.FirstOrDefault(IsImage)?.Url?.ToString();
        var hasVideo = quoted.Attachments.Any(a => a.ContentType?.StartsWith("video/") == true);

        if (quoted.Embeds.Count > 0)
        {
            var original = quoted.Embeds[0];
            if (!string.IsNullOrEmpty(original.Description)) embed.WithDescription(original.Description);
            if (original.Fields is not null)
                foreach (var field in original.Fields.Take(25))
                    embed.AddField(new DiscordEmbedField(field.Name, field.Value, field.Inline));

            var image = original.Image?.Url?.ToString() ?? firstImage;
            if (image is not null) embed.WithImageUrl(image);

            if (!string.IsNullOrEmpty(original.Footer?.Text)) footer = Truncate($"{original.Footer.Text} - {footer}", 2048);
        }
        else if (!string.IsNullOrEmpty(quoted.Content))
        {
            embed.WithDescription(quoted.Content.Length > 4089
                ? "\"" + quoted.Content[..4081] + "\" [...]"
                : "\"" + quoted.Content + "\"");
            if (firstImage is not null) embed.WithImageUrl(firstImage);
            else if (hasVideo) embed.AddField(new DiscordEmbedField("Anhang", "*Videos können nicht zitiert werden*"));
        }
        else if (quoted.Stickers?.FirstOrDefault()?.Url is { } stickerUrl)
        {
            embed.WithImageUrl(stickerUrl);
        }
        else if (firstImage is not null)
        {
            embed.WithImageUrl(firstImage);
        }
        else if (hasVideo)
        {
            embed.WithDescription("*Videos können nicht zitiert werden*");
        }

        return embed.WithFooter(footer).Build();
    }

    private static bool IsImage(DiscordAttachment attachment)
    {
        return attachment.ContentType?.StartsWith("image/") == true;
    }

    private static string Truncate(string value, int length)
    {
        return value.Length > length ? value[..length] : value;
    }
}
