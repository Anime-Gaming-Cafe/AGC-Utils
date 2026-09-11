#region

using AGC_Management.Entities.ApplicationSystem;
using AGC_Management.Services;

#endregion

namespace AGC_Management.Utils;

/// <summary>
///     Resolves the admin-managed placeholders first, then the built-in ones. Custom placeholders are plain
///     text snippets, so resolving them first lets a snippet itself contain a built-in placeholder.
/// </summary>
public static class TeamApplicationTemplateResolver
{
    public static readonly string[] BuiltIn =
        ["{user}", "{position}", "{phase}", "{decider}", "{decision}", "{datum}"];

    public static async Task<string> ResolveAsync(string text, TeamApplication? application = null,
        string? userName = null, string? deciderName = null)
    {
        if (string.IsNullOrEmpty(text)) return "";

        foreach (var (key, value) in await TeamApplicationService.GetPlaceholdersAsync())
            text = text.Replace("{" + key + "}", value);

        text = text.Replace("{user}", userName ?? "");
        text = text.Replace("{position}", application?.PositionName ?? "");
        text = text.Replace("{phase}", application?.PhaseName ?? "");
        text = text.Replace("{decider}", deciderName ?? "");
        text = text.Replace("{decision}", DescribeDecision(application));
        text = text.Replace("{datum}",
            ToolSet.GetFormattedTimeFromUnixAndRespectTimeZone(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));

        return text;
    }

    private static string DescribeDecision(TeamApplication? application)
    {
        return application?.Status switch
        {
            Enums.ApplicationSystem.TeamApplicationStatus.Angenommen => "angenommen",
            Enums.ApplicationSystem.TeamApplicationStatus.Abgelehnt => "abgelehnt",
            _ => ""
        };
    }
}
