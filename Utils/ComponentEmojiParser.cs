#region

using System.Text.RegularExpressions;

#endregion

namespace AGC_Management.Utils;

public static class ComponentEmojiParser
{
    private static readonly Regex CustomEmoji = new(@"^<a?:\w+:(\d+)>$", RegexOptions.Compiled);

    public static DiscordComponentEmoji? Parse(string? emoji)
    {
        if (string.IsNullOrWhiteSpace(emoji)) return null;

        var value = emoji.Trim();
        try
        {
            var match = CustomEmoji.Match(value);
            if (match.Success) return new DiscordComponentEmoji(ulong.Parse(match.Groups[1].Value));
            if (ulong.TryParse(value, out var id)) return new DiscordComponentEmoji(id);
            return new DiscordComponentEmoji(value);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
