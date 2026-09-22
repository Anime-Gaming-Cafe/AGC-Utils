namespace AGC_Management.Utils;

/// <summary>
///     What Discord actually accepts. One place, because the bot truncates against these numbers and the
///     dashboard counts against them, and the two drifting apart is how a panel silently loses text.
///     Discord counts UTF-16 code units, which is what <c>string.Length</c> returns, so no conversion.
/// </summary>
public static class DiscordLimits
{
    public const int EmbedTitle = 256;
    public const int EmbedDescription = 4096;
    public const int EmbedAuthorName = 256;
    public const int EmbedFooter = 2048;

    /// <summary>Sum of title, description, author, footer and all fields across every embed of a message.</summary>
    public const int EmbedTotal = 6000;

    public const int SelectPlaceholder = 150;
    public const int SelectOptionLabel = 100;
    public const int SelectOptionDescription = 100;
    public const int SelectOptionsPerMenu = 25;

    public const int ButtonLabel = 80;
    public const int ButtonsPerRow = 5;

    public const int ActionRowsPerMessage = 5;
    public const int CustomId = 100;

    /// <summary>The share of a limit at which the dashboard starts warning instead of staying neutral.</summary>
    public const double WarnRatio = 0.9;

    /// <summary>
    ///     Null when the value fits, otherwise the sentence to show the user. Naming the field matters:
    ///     a save blocked by "zu lang" with no field is a save nobody can fix.
    /// </summary>
    public static string? Check(string fieldName, string? value, int max)
    {
        var length = value?.Length ?? 0;
        return length <= max
            ? null
            : $"\"{fieldName}\" ist {length - max} Zeichen zu lang ({length} von {max}).";
    }
}
