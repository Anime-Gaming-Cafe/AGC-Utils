#region

using System.Globalization;
using System.Text;

#endregion

namespace AGC_Management.Utils;

/// <summary>
///     Turns a Discord activity name into comparable tokens and scores it against the patterns of a
///     selfrole option. Precision comes first: a wrong hit hands out a wrong role, while a missed hit
///     only costs a catalogue entry somebody binds by hand. That is why every match has to sit on word
///     boundaries - plain substring search let "Ark" match "Dark Souls".
/// </summary>
public static class ActivityMatcher
{
    /// <summary>Below this a match is treated as coincidence.</summary>
    public const double Threshold = 0.60;

    /// <summary>How far the winner has to be ahead, otherwise the case counts as ambiguous.</summary>
    public const double Margin = 0.10;

    private const double AnchorBonus = 0.25;

    /// <summary>Words that say something about the release, not about which game it is.</summary>
    private static readonly HashSet<string> Noise = new(StringComparer.Ordinal)
    {
        "edition", "remastered", "remaster", "definitive", "goty", "deluxe", "complete"
    };

    /// <summary>
    ///     Trailing phrases a recorder or overlay appends to the game name. Discord then reports
    ///     "Apex Legends with Medal", which is the same game and must not become its own catalogue entry.
    ///     Matched as a whole phrase at the end, so a title like "Medal of Honor" stays intact.
    /// </summary>
    private static readonly string[][] TrailingNoise =
    [
        ["with", "medal"]
    ];

    /// <summary>
    ///     Only the numerals that actually show up in titles. A general parser would read "mix" as 1009.
    /// </summary>
    private static readonly Dictionary<string, string> RomanNumerals = new(StringComparer.Ordinal)
    {
        ["i"] = "1", ["ii"] = "2", ["iii"] = "3", ["iv"] = "4", ["v"] = "5", ["vi"] = "6",
        ["vii"] = "7", ["viii"] = "8", ["ix"] = "9", ["x"] = "10", ["xi"] = "11", ["xii"] = "12"
    };

    public static string[] Tokenize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];

        var tokens = new List<string>();
        var current = new StringBuilder();

        foreach (var raw in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(raw) == UnicodeCategory.NonSpacingMark) continue;

            if (char.IsLetterOrDigit(raw))
            {
                current.Append(char.ToLowerInvariant(raw));
                continue;
            }

            Flush(tokens, current);
        }

        Flush(tokens, current);

        StripTrailingNoise(tokens);

        // Dropping every noise word would leave a title like "Definitive Edition" with nothing at all.
        var meaningful = tokens.Where(token => !Noise.Contains(token)).ToList();
        return [.. (meaningful.Count > 0 ? meaningful : tokens)];
    }

    private static void StripTrailingNoise(List<string> tokens)
    {
        foreach (var phrase in TrailingNoise)
        {
            if (tokens.Count <= phrase.Length) continue;

            var start = tokens.Count - phrase.Length;
            var matches = true;
            for (var i = 0; i < phrase.Length; i++)
                if (!string.Equals(tokens[start + i], phrase[i], StringComparison.Ordinal))
                {
                    matches = false;
                    break;
                }

            if (matches) tokens.RemoveRange(start, phrase.Length);
        }
    }

    private static void Flush(List<string> tokens, StringBuilder current)
    {
        if (current.Length == 0) return;

        var token = current.ToString();
        tokens.Add(RomanNumerals.TryGetValue(token, out var arabic) ? arabic : token);
        current.Clear();
    }

    /// <summary>The catalogue key of an activity: its tokens, joined. Two spellings of one game collapse.</summary>
    public static string Key(string? value)
    {
        return string.Concat(Tokenize(value));
    }

    /// <summary>The best score any of the patterns reaches, or 0 when none matches at all.</summary>
    public static double Score(IEnumerable<string> patterns, string activityName)
    {
        var activity = Tokenize(activityName);
        if (activity.Length == 0) return 0;

        var best = 0.0;
        foreach (var pattern in patterns)
        {
            var score = ScoreOne(Tokenize(pattern), activity);
            if (score > best) best = score;
        }

        return best;
    }

    private static double ScoreOne(string[] pattern, string[] activity)
    {
        if (pattern.Length == 0) return 0;

        var patternChars = pattern.Sum(token => token.Length);
        var activityChars = activity.Sum(token => token.Length);
        if (patternChars == 0 || activityChars == 0) return 0;

        // How much of the title the pattern actually accounts for. "Ark" explains little of
        // "Lost Ark", so the pattern "Lost Ark" has to win over it.
        var coverage = Math.Min(1.0, patternChars / (double)activityChars);

        var run = IndexOfRun(activity, pattern);
        if (run >= 0) return Combine(1.0, coverage, run == 0);

        var joined = string.Concat(activity);
        var squashed = string.Concat(pattern);
        var boundaries = Boundaries(activity);

        // "Garry's Mod" and "Counter-Strike 2" split differently than the patterns people type, so the
        // squashed forms are compared too - but only where the match covers whole words at both ends.
        // Without the end check "ark" would match "arknights" again.
        for (var index = joined.IndexOf(squashed, StringComparison.Ordinal);
             index >= 0;
             index = index + 1 <= joined.Length - squashed.Length
                 ? joined.IndexOf(squashed, index + 1, StringComparison.Ordinal)
                 : -1)
            if (boundaries.Contains(index) && boundaries.Contains(index + squashed.Length))
                return Combine(0.9, coverage, index == 0);

        return 0;
    }

    private static double Combine(double basis, double coverage, bool anchored)
    {
        return basis * (0.5 + 0.5 * coverage) + (anchored ? AnchorBonus : 0);
    }

    private static HashSet<int> Boundaries(string[] tokens)
    {
        var boundaries = new HashSet<int> { 0 };
        var offset = 0;
        foreach (var token in tokens)
        {
            offset += token.Length;
            boundaries.Add(offset);
        }

        return boundaries;
    }

    private static int IndexOfRun(string[] haystack, string[] needle)
    {
        for (var start = 0; start + needle.Length <= haystack.Length; start++)
        {
            var hit = true;
            for (var offset = 0; offset < needle.Length; offset++)
                if (!string.Equals(haystack[start + offset], needle[offset], StringComparison.Ordinal))
                {
                    hit = false;
                    break;
                }

            if (hit) return start;
        }

        return -1;
    }
}
