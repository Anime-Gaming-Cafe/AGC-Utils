#region

using System.Text.RegularExpressions;
using AGC_Management.Attributes;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Text;

#endregion

namespace AGC_Management.Eventlistener.NSFWScanner;

[EventHandler]
public class NSFWCheck : BaseCommandModule
{
    [Event]
    public async Task MessageCreated(DiscordClient client, MessageCreateEventArgs args)
    {
        _ = Task.Run(async () =>
        {
            if (args.Channel.Type == ChannelType.Private || args.Message.Author.IsBot) return;

            if (args.Guild.Id != ulong.Parse(BotConfig.GetConfig()["ServerConfig"]["ServerId"])) return;

            var isActivated = bool.Parse(BotConfig.GetConfig()["YukiMod"]["Active"]);
            if (!isActivated) return;

            if (args.Author.Id == 515404778021322773 || args.Author.Id == 856780995629154305) return;

            if (args.Channel.Id != 750365462524854331 || args.Channel.Id != 771174633230827540) return;

            using var _httpClient = new HttpClient();
            var apikey = BotConfig.GetConfig()["YukiMod"]["API-KEY"];
            _httpClient.DefaultRequestHeaders.Add("x-api-key", apikey);
            _httpClient.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/100.0.1000.0 Safari/537.36");

            var attachments = args.Message.Attachments;
            var urls = attachments.Select(att => att.Url.ToString()).ToList();

            if (!attachments.Any())
            {
                var text = args.Message.Content;
                var matches = Regex.Matches(text, @"(https?://)?(www\.)?([^\s]+)\.([^\s]+)");
                var urlsFromText = new List<string>();

                foreach (Match match in matches)
                {
                    var url = match.Value;
                    if (url.Contains(".png") || url.Contains(".jpg") || url.Contains(".jpeg") ||
                        url.Contains(".webp") || url.Contains(".gif"))
                        urlsFromText.Add(url);
                }

                urls.AddRange(urlsFromText);
            }

            foreach (var url in urls)
            {
                var content = new StringContent(JsonConvert.SerializeObject(new
                {
                    imageUrl = url,
                    userId = args.Author.Id.ToString()
                }), Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync("https://api.yukimod.com/scan/image", content);
                var responseString = await response.Content.ReadAsStringAsync();
                var json = JObject.Parse(responseString);
                var isNSFW = bool.Parse(json["detectionResult"]["nsfw"].ToString());

                if (isNSFW)
                {
                    var AlertChannel = ulong.Parse(BotConfig.GetConfig()["YukiMod"]["AlertChannel"]);
                    var c = args.Guild.GetChannel(AlertChannel);
                    var e = GetReportMessage(args.Message, args.Author);
                    await c.SendMessageAsync(e);

                    break;
                }
            }
        });
    }

    [Event]
    public async Task GuildMemberAdded(DiscordClient client, GuildMemberAddEventArgs args)
    {
        _ = Task.Run(async () =>
        {
            var isActivated = bool.Parse(BotConfig.GetConfig()["YukiMod"]["Active"]);
            if (!isActivated) return;

            if (args.Guild.Id != ulong.Parse(BotConfig.GetConfig()["ServerConfig"]["ServerId"])) return;


            using var _httpClient = new HttpClient();
            var apikey = BotConfig.GetConfig()["YukiMod"]["API-KEY"];
            _httpClient.DefaultRequestHeaders.Add("x-api-key", apikey);
            _httpClient.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/100.0.1000.0 Safari/537.36");

            var avatarUrl = args.Member.AvatarUrl;
            if (!avatarUrl.Contains(".png") || !avatarUrl.Contains(".jpg") || !avatarUrl.Contains(".jpeg") ||
                !avatarUrl.Contains(".webp"))
                return;

            var content = new StringContent(JsonConvert.SerializeObject(new
            {
                imageUrl = args.Member.AvatarUrl,
                userId = args.Member.Id.ToString()
            }), Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("https://api.yukimod.com/scan/image", content);
            var responseString = await response.Content.ReadAsStringAsync();
            var json = JObject.Parse(responseString);
            var isNSFW = bool.Parse(json["detectionResult"]["nsfw"].ToString());

            if (isNSFW)
            {
                var AlertChannel = ulong.Parse(BotConfig.GetConfig()["YukiMod"]["AlertChannel"]);
                var c = args.Guild.GetChannel(AlertChannel);
                var e = GetReportAvatarOnJoin(args.Member);
                await c.SendMessageAsync(e);
            }
        });
    }


    //[Event]
    public async Task GuildMemberUpdated(DiscordClient _client, GuildMemberUpdateEventArgs _args)
    {
        _ = Task.Run(async () =>
        {
            var isActivated = bool.Parse(BotConfig.GetConfig()["YukiMod"]["Active"]);
            if (!isActivated) return;


            if (_args.Guild.Id != GlobalProperties.AGCGuild.Id) return;

            using var _httpClient = new HttpClient();
            var apikey = BotConfig.GetConfig()["YukiMod"]["API-KEY"];
            _httpClient.DefaultRequestHeaders.Add("x-api-key", apikey);
            _httpClient.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/100.0.1000.0 Safari/537.36");

            var content = new StringContent(JsonConvert.SerializeObject(new
            {
                imageUrl = _args.AvatarUrlAfter,
                userId = _args.Member.Id.ToString()
            }), Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("https://api.yukimod.com/scan/image", content);
            var responseString = await response.Content.ReadAsStringAsync();
            var json = JObject.Parse(responseString);
            var isNSFW = bool.Parse(json["detectionResult"]["nsfw"].ToString());
            if (isNSFW)
            {
                var AlertChannel = ulong.Parse(BotConfig.GetConfig()["YukiMod"]["AlertChannel"]);
                var c = GlobalProperties.AGCGuild.GetChannel(AlertChannel);
                var e = GetReportAvatarOnMemberUpdate(_args.Member);
                await c.SendMessageAsync(e);
            }
        });
    }


    [Command("nsfwcheck")]
    [RequireStaffRole]
    public async Task NSFWCheckCommand(CommandContext ctx, bool activate)
    {
        BotConfig.SetConfig("YukiMod", "Active", activate.ToString());
        await ctx.RespondAsync($"NSFW Check wurde auf ``{activate}`` gesetzt!");
    }


    private static DiscordMessageBuilder GetReportAvatarOnMemberUpdate(DiscordUser user)
    {
        var embed = new DiscordEmbedBuilder()
            .WithAuthor("NSFW Inhalt erkannt!")
            .WithColor(DiscordColor.Red)
            .WithTimestamp(DateTime.Now)
            .WithFooter("Reported at")
            .WithThumbnail(user.AvatarUrl)
            .AddField(new DiscordEmbedField("User", $"{user.Mention} ``{user.Id}``"))
            .AddField(new DiscordEmbedField("Account erstellt", user.CreationTimestamp.Timestamp()))
            .AddField(new DiscordEmbedField("Avatar Link", user.AvatarUrl));

        var mb = new DiscordMessageBuilder()
            .AddEmbed(embed)
            .WithContent($"NSFW Inhalt von {user.Mention} wurde erkannt! **(Avatar)**");
        return mb;
    }

    private static DiscordMessageBuilder GetReportAvatarOnJoin(DiscordUser user)
    {
        var embed = new DiscordEmbedBuilder()
            .WithAuthor("NSFW Inhalt erkannt!")
            .WithColor(DiscordColor.Red)
            .WithTimestamp(DateTime.Now)
            .WithFooter("Reported at")
            .WithThumbnail(user.AvatarUrl)
            .AddField(new DiscordEmbedField("User", $"{user.Mention} ``{user.Id}``"))
            .AddField(new DiscordEmbedField("Account erstellt", user.CreationTimestamp.Timestamp()))
            .AddField(new DiscordEmbedField("Avatar Link", user.AvatarUrl));

        var mb = new DiscordMessageBuilder()
            .AddEmbed(embed)
            .WithContent($"NSFW Inhalt von {user.Mention} wurde erkannt! **(Avatar)**");
        return mb;
    }


    private static DiscordMessageBuilder GetReportMessage(DiscordMessage message, DiscordUser user)
    {
        var embed = new DiscordEmbedBuilder()
            .WithAuthor("Beleidigendes Verhalten erkannt!")
            .WithColor(DiscordColor.Red)
            .WithTimestamp(message.CreationTimestamp)
            .WithFooter("Reported at")
            .WithThumbnail(message.Author.AvatarUrl)
            .AddField(new DiscordEmbedField("Author", $"{message.Author.Mention} ``{message.Author.Id}``"))
            .AddField(new DiscordEmbedField("Channel", message.Channel.Mention))
            .AddField(new DiscordEmbedField("Message Link",
                $"https://discord.com/channels/{message.Guild.Id}/{message.Channel.Id}/{message.Id}"))
            .AddField(new DiscordEmbedField("Message Content",
                $"```{(string.IsNullOrWhiteSpace(message.Content) ? "none" : message.Content)}```"));


        var button = new DiscordLinkButtonComponent(
            $"https://discord.com/channels/{message.Guild.Id}/{message.Channel.Id}/{message.Id}", "Zur Nachricht");
        var mb = new DiscordMessageBuilder()
            .AddEmbed(embed)
            .WithReply(message.Id)
            .WithContent($"NSFW Inhalt von {user.Mention} wurde erkannt! **(Nachricht)**")
            .AddComponents(button);
        return mb;
    }
}
