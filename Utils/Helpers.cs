#region

using System.Globalization;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using AGC_Management.Entities;
using DisCatSharp.Net;
using Newtonsoft.Json;
using NpgsqlDataSource = Npgsql.NpgsqlDataSource;

#endregion

namespace AGC_Management.Utils;

public static class ToolSet
{
	public static string GetFaviconUrl() 
        => CurrentApplication.TargetGuild.IconUrl ?? "favicon.png";

    /// <summary>
    ///     The dashboard accent as space separated sRGB components for the --accent-rgb custom property,
    ///     taken from the same colour the bot puts on every Discord embed.
    /// </summary>
    public static string GetAccentRgb()
    {
        try
        {
            var color = BotConfig.GetEmbedColor();
            if (color.R == 0 && color.G == 0 && color.B == 0) return "47 132 162";

            return $"{color.R} {color.G} {color.B}";
        }
        catch (Exception)
        {
            return "47 132 162";
        }
    }

    /// <summary>
    ///     Display name for log tables. A cached user costs nothing; an uncached one is only fetched when the
    ///     caller asks, because a log of hundreds of rows must not turn into hundreds of REST calls.
    ///     This replaces three copies that had drifted apart on how migrated usernames were shown.
    /// </summary>
    public static async Task<string> ResolveUserNameAsync(ulong userId, bool fetch = false)
    {
        try
        {
            if (CurrentApplication.DiscordClient.UserCache.TryGetValue(userId, out var cached))
                return cached.IsMigrated ? cached.Username : cached.UsernameWithDiscriminator;

            if (!fetch) return userId.ToString();

            var user = await CurrentApplication.DiscordClient.GetUserAsync(userId);
            return user.IsMigrated ? user.Username : user.UsernameWithDiscriminator;
        }
        catch (Exception)
        {
            return userId.ToString();
        }
    }

	public static string GettextfromBase64(string base64)
    {
        var data = Convert.FromBase64String(base64);
        return Encoding.UTF8.GetString(data);
    }

    public static string GetFormattedTimeFromUnix(long unixTime)
    {
        var dateTimeOffset = DateTimeOffset.FromUnixTimeSeconds(unixTime);
        return dateTimeOffset.ToString("dd.MM.yyyy HH:mm:ss");
    }

    public static TimeSpan? ParseDuration(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        var text = input.Trim().ToLowerInvariant().Replace(" ", "");
        var total = TimeSpan.Zero;
        var digits = new StringBuilder();
        var unit = new StringBuilder();

        foreach (var c in text)
        {
            if (char.IsDigit(c))
            {
                if (unit.Length > 0)
                {
                    if (!TryAppendDurationPart(digits.ToString(), unit.ToString(), ref total)) return null;
                    digits.Clear();
                    unit.Clear();
                }

                digits.Append(c);
                continue;
            }

            if (digits.Length == 0) return null;
            unit.Append(c);
        }

        if (digits.Length == 0 || unit.Length == 0) return null;
        if (!TryAppendDurationPart(digits.ToString(), unit.ToString(), ref total)) return null;

        return total > TimeSpan.Zero ? total : null;
    }

    private static bool TryAppendDurationPart(string digits, string unit, ref TimeSpan total)
    {
        if (!long.TryParse(digits, out var amount) || amount < 0) return false;

        var span = unit switch
        {
            "s" or "sec" => TimeSpan.FromSeconds(amount),
            "m" or "min" => TimeSpan.FromMinutes(amount),
            "h" or "std" => TimeSpan.FromHours(amount),
            "d" or "t" => TimeSpan.FromDays(amount),
            "w" => TimeSpan.FromDays(amount * 7),
            "mo" => TimeSpan.FromDays(amount * 30),
            "y" or "j" => TimeSpan.FromDays(amount * 365),
            _ => TimeSpan.MinValue
        };

        if (span == TimeSpan.MinValue) return false;

        total += span;
        return true;
    }


    public static string GetFormattedTimeFromUnixAndRespectTimeZone(long unixTime)
    {
        var dateTimeOffset = DateTimeOffset.FromUnixTimeSeconds(unixTime);
        return TimeZoneInfo.ConvertTimeFromUtc(dateTimeOffset.DateTime, TimeZoneInfo.Local)
            .ToString("dd.MM.yyyy HH:mm:ss");
    }


    public static async Task<bool> IsUserInCache(ulong userId)
    {
        try
        {
            var cachedmembers = CurrentApplication.DiscordClient.UserCache.Values.ToList();

            return cachedmembers.Any(member => member.Id == userId);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Fehler beim Abrufen der Servermitglieder: " + ex.Message);
            return false;
        }
    }


    public static string GetBuildNumber(Assembly assembly)
    {
        const string BuildVersionMetadataPrefix = "+build";

        var attribute = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        if (attribute?.InformationalVersion != null)
        {
            var value = attribute.InformationalVersion;
            var index = value.IndexOf(BuildVersionMetadataPrefix);
            if (index > 0) return value[(index + BuildVersionMetadataPrefix.Length)..];
        }

        return string.Empty;
    }

    public static string RemoveWhitespace(string input)
    {
        return new string([.. input.ToCharArray().Where(c => !char.IsWhiteSpace(c))]);
    }

    public static long GetBuildDateToUnixTime(Assembly assembly)
    {
        const string BuildVersionMetadataPrefix = "+build";

        var attribute = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        if (attribute?.InformationalVersion != null)
        {
            var value = attribute.InformationalVersion;
            var index = value.IndexOf(BuildVersionMetadataPrefix);
            if (index > 0)
            {
                value = value[(index + BuildVersionMetadataPrefix.Length)..];
                if (DateTime.TryParseExact(value, "yyyyMMddHHmmss", CultureInfo.CurrentCulture,
                        DateTimeStyles.AssumeLocal, out var result))
                    return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(result, TimeZoneInfo.Local))
                        .ToUnixTimeSeconds();
            }
        }

        return default;
    }

    public static async Task<bool> IsUserOnServer(ulong userId)
    {
        try
        {
            var serverMembers = CurrentApplication.TargetGuild.Members.Values.ToList();

            return serverMembers.Any(member => member.Id == userId);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Fehler beim Abrufen der Servermitglieder: " + ex.Message);
            return false;
        }
    }

    public static PartialUser GetFallbackUser(ulong userId)
    {
        return new PartialUser
        {
            UserId = userId,
            UserName = userId.ToString(),
            Avatar = GetDefaultAvatarUrlForUserId(userId)
        };
    }

    public static string GetDefaultAvatarUrlForUserId(ulong userId)
    {
        var domainUrl = DiscordDomain.GetDomain(CoreDomain.DiscordCdn).Url;
        var avatarIndex = (userId >> 22) % 6;
        var avatarUrl = $"{domainUrl}{Endpoints.EMBED}{Endpoints.AVATARS}/{avatarIndex}.png?size=1024";

        return avatarUrl;
    }

    public static string GetFormattedName(ulong userid)
    {
        var member = CurrentApplication.TargetGuild.Members.Values.FirstOrDefault(x => x.Id == userid);
        if (member != null)
        {
            if (!string.IsNullOrEmpty(member.Nickname)) return $"{member.Username} ({member.Nickname})";

            if (!string.IsNullOrEmpty(member.DisplayName)) return $"{member.Username} ({member.DisplayName})";

            return member.Username;
        }

        return userid.ToString();
    }

    public static string GetFormattedName(DiscordMember member)
    {
        if (!string.IsNullOrEmpty(member.Nickname)) return $"{member.Username} ({member.Nickname})";

        if (!string.IsNullOrEmpty(member.DisplayName)) return $"{member.Username} ({member.DisplayName})";

        return member.Username;
    }

    public static ulong GetUserIdFromHttpContext(HttpContext? context)
        => GetUserIdFromPrincipal(context?.User);

    /// <summary>
    ///     Variant for Blazor components: the HttpContext is null there outside of the prerender,
    ///     the ClaimsPrincipal comes from the cascading AuthenticationState instead.
    /// </summary>
    public static ulong GetUserIdFromPrincipal(ClaimsPrincipal? principal)
    {
        var userId = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Convert.ToUInt64(userId);
    }


    public static async Task<List<BannSystemWarn>?> GetBannsystemWarns(DiscordUser user)
    {
        using HttpClient client = new();
        var apiKey = GlobalProperties.DebugMode
            ? BotConfig.GetConfig()["ModHQConfigDBG"]["API_Key"]
            : BotConfig.GetConfig()["ModHQConfig"]["API_Key"];
        var apiUrl = GlobalProperties.DebugMode
            ? BotConfig.GetConfig()["ModHQConfigDBG"]["API_URL"]
            : BotConfig.GetConfig()["ModHQConfig"]["API_URL"];

        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", apiKey);
        var response = await client.GetAsync($"{apiUrl}{user.Id}");
        if (response.IsSuccessStatusCode)
        {
            var json = await response.Content.ReadAsStringAsync();
            var apiResponse = JsonConvert.DeserializeObject<UserInfoApiResponse>(json);
            var data = apiResponse.warns;
            return data;
        }

        return null;
    }

    public static async Task<List<BannSystemReport?>?> GetBannsystemReports(DiscordUser user)
    {
        using HttpClient client = new();
        var apiKey = GlobalProperties.DebugMode
            ? BotConfig.GetConfig()["ModHQConfigDBG"]["API_Key"]
            : BotConfig.GetConfig()["ModHQConfig"]["API_Key"];
        var apiUrl = GlobalProperties.DebugMode
            ? BotConfig.GetConfig()["ModHQConfigDBG"]["API_URL"]
            : BotConfig.GetConfig()["ModHQConfig"]["API_URL"];

        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", apiKey);
        var response = await client.GetAsync($"{apiUrl}{user.Id}");
        if (response.IsSuccessStatusCode)
        {
            var json = await response.Content.ReadAsStringAsync();
            var apiResponse = JsonConvert.DeserializeObject<UserInfoApiResponse>(json);
            var data = apiResponse.reports;
            return data;
        }

        return null;
    }

    public static async Task<List<BannSystemReport?>?> BSReportToWarn(DiscordUser user)
    {
        try
        {
            var data = await GetBannsystemReports(user);

            return [.. data.Select(warn => new BannSystemReport
            {
                reportId = warn.reportId,
                authorId = warn.authorId,
                reason = warn.reason,
                timestamp = warn.timestamp,
                active = warn.active
            })];
        }
        catch (Exception e)
        {
            // ignored
        }

        return [];
    }

    public static async Task<List<BannSystemWarn>> BSWarnToWarn(DiscordUser user)
    {
        try
        {
            var data = await GetBannsystemWarns(user);

            return [.. data.Select(warn => new BannSystemWarn
            {
                warnId = warn.warnId,
                authorId = warn.authorId,
                reason = warn.reason,
                timestamp = warn.timestamp
            })];
        }
        catch (Exception e)
        {
            // ignored
        }

        return [];
    }


    public static bool HasActiveBannSystemReport(List<BannSystemReport> reports)
    {
        return reports.Any(report => report.active);
    }

    public static async Task<bool> CheckForReason(CommandContext ctx, string? reason)
    {
        if (reason == null)
        {
            var embedBuilder = new DiscordEmbedBuilder().WithTitle("Fehler: Kein Grund angegeben!")
                .WithDescription("Bitte gebe einen Grund an")
                .WithColor(DiscordColor.Red).WithFooter($"{ctx.User.UsernameWithDiscriminator}", ctx.User.AvatarUrl);
            var msg = new DiscordMessageBuilder().AddEmbed(embedBuilder.Build()).WithReply(ctx.Message.Id);
            await ctx.Channel.SendMessageAsync(msg);


            return true;
        }

        if (reason == "")
        {
            var embedBuilder = new DiscordEmbedBuilder().WithTitle("Fehler: Kein Grund angegeben!")
                .WithDescription("Bitte gebe einen Grund an")
                .WithColor(DiscordColor.Red).WithFooter($"{ctx.User.UsernameWithDiscriminator}", ctx.User.AvatarUrl);
            var msg = new DiscordMessageBuilder().AddEmbed(embedBuilder.Build()).WithReply(ctx.Message.Id);
            await ctx.Channel.SendMessageAsync(msg);
            return true;
        }

        return false;
    }

    public static async Task<long> GetTicketCount(ulong userid)
    {
        try
        {
            var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
            await using var cmd =
                con.CreateCommand($"SELECT COUNT(*) FROM ticketstore WHERE ticket_owner = {userid}");
            var result = await cmd.ExecuteScalarAsync();
            return (long)result;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return 0;
        }
    }

    public static IEnumerable<DiscordOverwriteBuilder> MergeOverwrites(DiscordChannel userChannel,
        List<DiscordOverwriteBuilder> overwrites,
        out IEnumerable<DiscordOverwriteBuilder> targetOverwrites)
    {
        targetOverwrites = userChannel.PermissionOverwrites.Select(x => x.ConvertToBuilder());
        foreach (var overwrite in overwrites)
            targetOverwrites =
                targetOverwrites.Merge(overwrite.Type, overwrite.Target, overwrite.Allowed, overwrite.Denied);

        var newOverwrites = targetOverwrites.ToList();
        return newOverwrites;
    }


    /// <summary>
    ///     Builds an absolute dashboard link. Both keys are optional in config.ini, so each read falls back
    ///     the same way Program.RunAspAsync does when it rewrites the request host.
    /// </summary>
    public static string GetDashboardUrl(string relativePath = "")
    {
        bool useHttps;
        try
        {
            useHttps = bool.Parse(BotConfig.GetConfig()["WebUI"]["UseHttps"]);
        }
        catch
        {
            useHttps = false;
        }

        string dashboardUrl;
        try
        {
            dashboardUrl = BotConfig.GetConfig()["WebUI"]["DashboardURL"];
        }
        catch
        {
            dashboardUrl = "localhost";
        }

        var path = relativePath.TrimStart('/');
        var baseUrl = $"{(useHttps ? "https" : "http")}://{dashboardUrl.TrimEnd('/')}";
        return string.IsNullOrEmpty(path) ? baseUrl : $"{baseUrl}/{path}";
    }

    public static string GenerateCaseID()
    {
        var guid = Guid.NewGuid().ToString("N");
        var uniqueID = guid[..8];
        return uniqueID;
    }

    public static async Task<bool> UserHasClosedPendingTicket(ulong UserId)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        var query = $"SELECT COUNT(*) FROM ticketcache WHERE ticket_owner = '{(long)UserId}'";
        await using var cmd = con.CreateCommand(query);
        var rowCount = Convert.ToInt32(cmd.ExecuteScalar());
        if (rowCount > 0) return true;


        return false;
    }

    public static async Task<bool> UserHasOpenTicket(ulong UserId)
    {
        var connection = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        var query = $"SELECT COUNT(*) FROM ticketcache WHERE ticket_users @> ARRAY[{(long)UserId}::bigint]";
        await using var cmd = connection.CreateCommand(query);
        var rowCount = Convert.ToInt32(cmd.ExecuteScalar());
        if (rowCount > 0) return true;

        return false;
    }

    public static async Task<int> GenerateBanDeleteMessageSeconds(ulong UserId)
    {
        var hasOpenTicket = await UserHasOpenTicket(UserId);
        var hasClosedPendingTicket = await UserHasClosedPendingTicket(UserId);
        if (hasOpenTicket) return 0;

        return hasClosedPendingTicket ? 0 : 604800; // 7 Tage in Sekunden
    }


    public static async Task SendWarnAsChannel(CommandContext ctx, DiscordUser user, DiscordEmbed uembed, string caseid)
    {
        var userembed = uembed;
        var catid = BotConfig.GetConfig()["TicketConfig"]["SupportCategoryId"];
        var wchannel = await ctx.Guild.CreateChannelAsync($"warn-{caseid}",
            ChannelType.Text, ctx.Guild.GetChannel(Convert.ToUInt64(catid)), user.Id.ToString());
        await wchannel.AddOverwriteAsync(await user.ConvertToMember(ctx.Guild),
            Permissions.AccessChannels, Permissions.SendMessages | Permissions.AddReactions, "Warn eröffnet");
        var buttonack = new DiscordButtonComponent(ButtonStyle.Primary, "ackwarn", "Kenntnisnahme",
            emoji: new DiscordComponentEmoji(DiscordEmoji.FromName(ctx.Client, ":white_check_mark:")));
        var mb = new DiscordMessageBuilder()
            .AddEmbed(userembed).AddComponents(buttonack).WithContent(user.Mention);
        await wchannel.SendMessageAsync(mb);
    }

    public static async Task<bool> TicketUrlCheck(CommandContext ctx, string reason)
    {
        var TicketUrl = "ticketsystem.animegamingcafe.de";
        if (reason == null) return false;
        if (reason.Contains(TicketUrl, StringComparison.InvariantCultureIgnoreCase))
        {
            Console.WriteLine("Ticket-URL enthalten");
            var embedBuilder = new DiscordEmbedBuilder().WithTitle("Fehler: Ticket-URL enthalten")
                .WithDescription("Bitte schreibe den Grund ohne Ticket-URL").WithColor(DiscordColor.Red);
            var embed = embedBuilder.Build();
            var msg_e = new DiscordMessageBuilder().AddEmbed(embed).WithReply(ctx.Message.Id);
            await ctx.Channel.SendMessageAsync(msg_e);

            return true;
        }

        return false;
    }
}