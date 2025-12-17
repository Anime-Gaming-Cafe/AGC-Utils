#region

using System.Text.RegularExpressions;
using AGC_Management.Utils;
using DisCatSharp.Exceptions;

#endregion

namespace AGC_Management;

internal static class DiscordExtension
{
    private static readonly Regex InviteRegex =
        new(RegexPatterns.INVITE, RegexOptions.Compiled | RegexOptions.IgnoreCase);

    internal static List<DiscordOverwriteBuilder> ConvertToBuilderWithNewOverwrites(
        this IReadOnlyList<DiscordOverwrite> overwrites, DiscordMember member, Permissions allowed, Permissions denied)
    {
        return overwrites.Where(x => x.Id != member.Id)
            .Select(x => x.Type == OverwriteType.Role
                ? new DiscordOverwriteBuilder(x.GetRoleAsync().Result) { Allowed = x.Allowed, Denied = x.Denied }
                : new DiscordOverwriteBuilder(x.GetMemberAsync().Result) { Allowed = x.Allowed, Denied = x.Denied })
            .Append(new DiscordOverwriteBuilder(member)
            {
                Allowed = (overwrites.FirstOrDefault(x => x.Id == member.Id, null)?.Allowed ?? Permissions.None) |
                          allowed,
                Denied = (overwrites.FirstOrDefault(x => x.Id == member.Id, null)?.Denied ?? Permissions.None) | denied
            }).ToList();
    }

    public static string NameWithRemovedRegionSuffix(this DiscordGuild guild)
    {
        return guild.Name.EndsWith("[DE/GER]") ? guild.Name.Replace("[DE/GER]", "") : guild.Name;
    }


    public static string ConvertMarkdownToHtml(this string? md, bool isEmbed = false)
    {
        if (string.IsNullOrWhiteSpace(md))
            return md;

        md = md.ReplaceLineEndings("\n");

        if (isEmbed)
            md = string.Join("\n", md.Split("\n").Select(x => $"<div>{x}</div>"));

        md = Regex.Replace(md, @"(?<!\\)\`([^\n`]+?)\`", e =>
        {
            return $"<discord-inline-code in-embed=\"{isEmbed.ToString().ToLower()}\" >{e.Groups[1].Value
                .Replace("*", "\\*").Replace("_", "\\_").Replace("&gt;", "\\&gt;").Replace("&lt;", "\\&lt;").Replace("~", "\\~").Replace("`", "\\`").Replace("|", "\\|").Replace(" ", "&nbsp;")}</discord-inline-code>";
        }, RegexOptions.Compiled);

        md = Regex.Replace(md, @"(?<!\\)(?:\`\`\`)(?:(\w{2,15})\n)?((?:.|\n)+?)(?:\`\`\`)", e =>
        {
            var lang = "";

            if (e.Groups[1].Success)
                lang = e.Groups[1].Value;

            return
                $"<discord-code-block language=\"{lang}\"><pre>{e.Groups[2].Value.Replace(" ", "&nbsp;")}</pre></discord-code-block>";
        }, RegexOptions.Compiled | RegexOptions.Multiline);

        md = Regex.Replace(md, @"(?<!\\)\*\*([^\n*]+?)(?<!\\)\*\*", e =>
        {
            return $"<discord-bold>{e.Groups[1].Value
                .Replace("*", "\\*").Replace("_", "\\_").Replace("&gt;", "\\&gt;").Replace("&lt;", "\\&lt;").Replace("~", "\\~").Replace("`", "\\`").Replace("|", "\\|")}</discord-bold>";
        }, RegexOptions.Compiled);

        md = Regex.Replace(md, @"(?<!\\)\~\~([^\n~]+?)(?<!\\)\~\~", e => { return $"<s>{e.Groups[1].Value}</s>"; },
            RegexOptions.Compiled);


        md = Regex.Replace(md, @"(?<!\\)__([^\n~]+?)(?<!\\)__", e => { return $"<u>{e.Groups[1].Value}</u>"; },
            RegexOptions.Compiled);

        md = Regex.Replace(md, @"(?<!\\)\|\|([^\n|]+?)(?<!\\)\|\|",
            e => { return $"<discord-spoiler>{e.Groups[1].Value}</discord-spoiler>"; }, RegexOptions.Compiled);

        md = Regex.Replace(md, @"(?<!\\)\*([^\n*]+?)(?<!\\)\*",
            e => { return $"<discord-italic>{e.Groups[1].Value}</discord-italic>"; }, RegexOptions.Compiled);

        md = Regex.Replace(md, @"(?<![\\_])_([^\n_]+?)(?<!\\)_",
            e => { return $"<discord-italic>{e.Groups[1].Value}</discord-italic>"; }, RegexOptions.Compiled);

        md = Regex.Replace(md, @"^(?<!\\)&gt; ([^\n_]+?)",
            e => { return $"<discord-quote>{e.Groups[1].Value}</discord-quote>"; },
            RegexOptions.Compiled | RegexOptions.Multiline);

        md = Regex.Replace(md, @"(?<!\\)&lt;t:(\d+?)(:(\w))?&gt;",
            e =>
            {
                return
                    $"<discord-time format=\"{e.Groups[3].Value}\" timestamp=\"{e.Groups[1].Value}\"></discord-time>";
            }, RegexOptions.Compiled);

        md = Regex.Replace(md, @"(?<!\\)&lt;/([\w -]+?):(?:\d+?)&gt;",
            e => { return $"<discord-mention type=\"slash\">{e.Groups[1].Value}</discord-mention>"; },
            RegexOptions.Compiled);

        md = Regex.Replace(md, @"^(?<!\\)(\#{1,6})\s*(.+)$", match =>
        {
            var level = match.Groups[1].Value.Length;
            var content = match.Groups[2].Value.Trim();
            return $"<h{level}>{content}</h{level}>";
        }, RegexOptions.Multiline | RegexOptions.Compiled);


        md = Regex.Replace(md,
            @"(&lt;)?(https?:\/\/(www\.)?[-a-zA-Z0-9@:%._\+~#=]{1,256}\.[a-zA-Z0-9()]{1,6}\b([-a-zA-Z0-9()@:%_\+.~#?&//=;]*))",
            e =>
            {
                var url = e.Groups[2].Value;

                if ((e.Groups[1]?.Success ?? false) && url.Contains("&gt;"))
                    url = url[..url.IndexOf("&gt;")];

                return $"<a target=\"_blank\" href=\"{url}\">{url}</a>";
            }, RegexOptions.Compiled);

        md = Regex.Replace(md, @"(?<!\\)&lt;@(?:!)?(\d+?)&gt;", e =>
        {
            try
            {
                var id = Convert.ToUInt64(e.Groups[1].Value);
                return
                    $"<discord-mention>{CurrentApplication.DiscordClient!.GetUserAsync(id).GetAwaiter().GetResult().GetFormattedUserName()}</discord-mention>";
            }
            catch (Exception)
            {
                return $"@{e.Groups[1].Value}";
            }
        }, RegexOptions.Compiled);


        md = Regex.Replace(md, @"(?<!\\)&lt;#(?:!)?(\d+?)&gt;", e =>
        {
            try
            {
                var id = Convert.ToUInt64(e.Groups[1].Value);
                var channel = CurrentApplication.DiscordClient!.GetChannelAsync(id).GetAwaiter().GetResult();
                var type = channel.Type switch
                {
                    ChannelType.Voice => "voice",
                    ChannelType.Stage => "voice",
                    ChannelType.Forum => "forum",
                    ChannelType.GuildMedia => "forum",
                    ChannelType.PublicThread => "thread",
                    ChannelType.PrivateThread => "thread",
                    ChannelType.NewsThread => "thread",
                    _ => "channel"
                };


                return $"<discord-mention type=\"{type}\">{channel.Name}</discord-mention>";
            }
            catch (Exception)
            {
                return $"@{e.Groups[1].Value}";
            }
        }, RegexOptions.Compiled);

        md = Regex.Replace(md, @"(?<!\\)&lt;(a)?:(\w+?):(\d+?)&gt;", e =>
        {
            var url = $"https://cdn.discordapp.com/emojis/{e.Groups[3].Value}.{(e.Groups[1].Success ? "gif" : "png")}";

            return $"<discord-custom-emoji name=\"{e.Groups[2].Value}\" url=\"{url}\"></discord-custom-emoji>";
        }, RegexOptions.Compiled);

        md = Regex.Replace(md, @"(?<!\\)(?<!\&gt;)(?<!a):\w+?:", e =>
        {
            if (!DiscordEmoji.TryFromName(CurrentApplication.DiscordClient, e.Value, out var emoji))
                return e.Value;
            try
            {
                return $"{emoji.UnicodeEmoji}";
            }
            catch (Exception)
            {
                return e.Value;
            }
        }, RegexOptions.Compiled);

        md = md.Replace("\\*", "*");
        md = md.Replace("\\_", "_");
        md = md.Replace("\\&gt;", "&gt;");
        md = md.Replace("\\&lt;", "&lt;");
        md = md.Replace("\\~", "~");
        md = md.Replace("\\`", "`");
        md = md.Replace("\\|", "|");

        return md; // .ReplaceLineEndings("<br />")
    }


    internal static List<DiscordOverwriteBuilder> ConvertToBuilderWithNewOverwrites(
        this IReadOnlyList<DiscordOverwrite> overwrites, DiscordRole role, Permissions allowed, Permissions denied)
    {
        return overwrites.Where(x => x.Id != role.Id)
            .Select(x => x.Type == OverwriteType.Role
                ? new DiscordOverwriteBuilder(x.GetRoleAsync().Result) { Allowed = x.Allowed, Denied = x.Denied }
                : new DiscordOverwriteBuilder(x.GetMemberAsync().Result) { Allowed = x.Allowed, Denied = x.Denied })
            .Append(new DiscordOverwriteBuilder(role)
            {
                Allowed =
                    (overwrites.FirstOrDefault(x => x.Id == role.Id, null)?.Allowed ?? Permissions.None) | allowed,
                Denied = (overwrites.FirstOrDefault(x => x.Id == role.Id, null)?.Denied ?? Permissions.None) | denied
            }).ToList();
    }

    internal static List<DiscordOverwriteBuilder> ConvertToBuilder(this IReadOnlyList<DiscordOverwrite> overwrites)
    {
        return overwrites.Select(x =>
                x.Type == OverwriteType.Role
                    ? new DiscordOverwriteBuilder(x.GetRoleAsync().Result) { Allowed = x.Allowed, Denied = x.Denied }
                    : new DiscordOverwriteBuilder(x.GetMemberAsync().Result) { Allowed = x.Allowed, Denied = x.Denied })
            .ToList();
    }


    internal static async Task<DiscordUser?> TryGetUserAsync(this DiscordClient client, ulong userId, bool fetch = true)
    {
        try
        {
            return await client.GetUserAsync(userId, fetch).ConfigureAwait(false);
        }
        catch (NotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    ///     Truncates a string to a maximum length.
    /// </summary>
    /// <param name="value"></param>
    /// <param name="maxLength"></param>
    /// <returns>string</returns>
    public static string Truncate(this string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value; // Return original value if it's null or empty
        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }

    internal static async Task<DiscordChannel?> TryGetChannelAsync(this DiscordClient client, ulong id,
        bool fetch = true)
    {
        try
        {
            return await client.GetChannelAsync(id, fetch).ConfigureAwait(false);
        }
        catch (NotFoundException)
        {
            return null;
        }
    }


    public static async Task<List<string>> GetInvitesAsync(this DiscordMessage message, DiscordClient client)
    {
        var matches = InviteRegex.Matches(message.Content);
        var invites = new List<string>();

        foreach (Match match in matches)
            if (match.Groups["code"].Success)
            {
                var code = match.Groups["code"].Value;
                var invite = await client.GetInviteByCodeAsync(code);

                if (invite != null) invites.Add(invite.ToString());
            }

        return invites;
    }

    public static List<string> GetInvites(this DiscordMessage message)
    {
        var matches = InviteRegex.Matches(message.Content);
        var invites = new List<string>();

        foreach (Match match in matches)
            if (match.Groups["code"].Success)
            {
                var code = match.Groups["code"].Value;
                var inviteLink = $"https://discord.gg/{code}";
                invites.Add(inviteLink);
            }

        return invites;
    }
}