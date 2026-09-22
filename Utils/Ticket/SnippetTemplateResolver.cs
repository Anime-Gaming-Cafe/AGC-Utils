#region

using AGC_Management.Helper;
using AGC_Management.Managers;

#endregion

namespace AGC_Management.Utils;

/// <summary>
///     Fills the placeholders of a snippet from the ticket it is being sent into. Same shape as
///     <see cref="TeamApplicationTemplateResolver" />, including the BuiltIn list, so the dashboard can
///     render the available placeholders from here instead of repeating them in a hint text.
/// </summary>
public static class SnippetTemplateResolver
{
    public static readonly (string Token, string Description)[] BuiltIn =
    [
        ("{user}", "Erwähnt den Ticket-Ersteller, pingt ihn also an."),
        ("{username}", "Name des Ticket-Erstellers, ohne Ping."),
        ("{team}", "Erwähnt das Teammitglied, das das Snippet gerade sendet."),
        ("{teamname}", "Name dieses Teammitglieds, ohne Ping."),
        ("{claimer}", "Erwähnt den, der das Ticket geclaimed hat. Leer, wenn es niemand geclaimed hat."),
        ("{unixtimestamp}", "Aktuelle Zeit als Unix-Zeitstempel."),
        ("{unixtimestamp+3600}", "Zeit in einer Stunde. Beliebige Sekundenzahl möglich. Für einen echten Discord-Zeitstempel: <t:{unixtimestamp+3600}:R>")
    ];

    public static async Task<string> ResolveAsync(string text, DiscordChannel ticketChannel, DiscordUser sender)
    {
        if (string.IsNullOrEmpty(text)) return "";

        if (NeedsTicketContext(text))
        {
            var ownerId = (ulong)await TicketManagerHelper.GetTicketOwnerFromChannel(ticketChannel);
            var state = await TicketManagerHelper.ReadClaimStateAsync(ticketChannel.Id);

            text = text.Replace("{user}", ownerId > 0 ? $"<@{ownerId}>" : "");
            text = text.Replace("{username}",
                ownerId > 0 ? await ToolSet.ResolveUserNameAsync(ownerId, true) : "");
            text = text.Replace("{claimer}",
                state is { Claimed: true, ClaimedFrom: > 0 } ? $"<@{state.ClaimedFrom}>" : "");
        }

        text = text.Replace("{team}", sender.Mention);
        text = text.Replace("{teamname}", sender.GetFormattedUserName());

        return SnippetManagerHelper.FormatStringWithVariables(text);
    }

    /// <summary>Saves two database reads for snippets that only use the sender or the timestamp.</summary>
    private static bool NeedsTicketContext(string text)
    {
        return text.Contains("{user}") || text.Contains("{username}") || text.Contains("{claimer}");
    }
}
