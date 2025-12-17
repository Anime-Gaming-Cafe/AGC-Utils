using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AGC_Management.Entities.TicketSuche;
using HtmlAgilityPack;

namespace AGC_Management.Utils;

public static class TicketSearchTools
{
    public static List<TicketSearchEntry> SearchTickets = new();
    private static readonly string FileEnding = ".html";
    public static bool ScanDone { get; private set; }

    public static async Task LoadTicketsIntoCache()
    {
        var logger = CurrentApplication.Logger;
        logger.Information("[TicketSuche Cache] Lade Tickets...");
        var completePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), "data/tickets/transcripts");
        logger.Information("[TicketSuche Cache] Lade Tickets: " + completePath);
        if (!Directory.Exists(completePath))
        {
            logger.Error("[TicketSuche Cache] Ticket search path does not exist: " + completePath);
            return;
        }

        var files = Directory.GetFiles(completePath, "*" + FileEnding);
        logger.Information("[TicketSuche Cache] Gefundene Tickets: " + files.Length);
        foreach (var file in files) await ProcessAndAddFileToCache(file);
        logger.Information("[TicketSuche Cache] Tickets geladen: " + SearchTickets.Count);
        ScanDone = true;
    }

    public static Task LoadSingleTicketIntoCache(string ticketFileName)
    {
        _ = Task.Run(async () =>
        {
            var logger = CurrentApplication.Logger;
            var completePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), "data/tickets/transcripts");
            var file = Path.Combine(completePath, ticketFileName);
            logger.Information("[TicketSuche Cache] Lade Ticket: " + file);
            if (!File.Exists(file))
            {
                logger.Error("[TicketSuche Cache] Ticket does not exist: " + file);
                return;
            }

            await ProcessAndAddFileToCache(file);
            logger.Information("[TicketSuche Cache] Ticket geladen: " + ticketFileName);
        });
        return Task.CompletedTask;
    }

    private static async Task ProcessAndAddFileToCache(string file)
    {
        using var md5 = MD5.Create();
        await using var stream = File.OpenRead(file);
        var hash = await md5.ComputeHashAsync(stream);
        var fileHash = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

        if (SearchTickets.Any(ticket => ticket.FileHash == fileHash)) return;

        var entry = new TicketSearchEntry
        {
            FileName = Path.GetFileName(file),
            FileHash = fileHash,
            HtmlContent = await File.ReadAllTextAsync(file)
        };
        SearchTickets.Add(entry);
    }

    /// <summary>
    ///     Searches tickets by query and returns a list of matching results. "filename", "title" and "snippet" are the keys of
    ///     the dictionaries.
    /// </summary>
    /// <param name="query">The query to search for.</param>
    /// <returns>
    ///     A list of dictionaries containing the ticket information. Each dictionary contains the following key-value
    ///     pairs: "fileName", "title", "snippet".
    /// </returns>
    public static List<Dictionary<string, string>> SearchTicketsByQuery(string query)
    {
        var results = new List<Dictionary<string, string>>();

        foreach (var ticket in SearchTickets)
        {
            HtmlDocument doc = new();
            var stream = new MemoryStream(Encoding.Default.GetBytes(ticket.HtmlContent));
            doc.Load(stream);

            stream.Close();
            stream.Dispose();

            var text = doc.DocumentNode.InnerText;
            if (text.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
            var result = new Dictionary<string, string>
            {
                { "fileName", ticket.FileName },
                { "title", doc.DocumentNode.SelectSingleNode("//title")?.InnerText ?? "No Title" },
                { "snippet", CreateHighlightedPreview(text, query, 500) }
            };
            if (!result["title"].Contains("closed", StringComparison.OrdinalIgnoreCase))
                continue;
            results.Add(result);
            doc = null;
        }

        return results;
    }

    private static string CreateHighlightedPreview(string text, string query, int maxLength = 100)
    {
        var index = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (index != -1)
        {
            var start = Math.Max(index - maxLength / 2, 0);
            var end = Math.Min(index + maxLength / 2, text.Length);
            var preview = text.Substring(start, end - start);
            return HighlightQuery(preview, query);
        }
        else
        {
            var preview = text.Length > maxLength ? text.Substring(0, maxLength) : text;
            return HighlightQuery(preview, query);
        }
    }

    private static string HighlightQuery(string text, string query)
    {
        var regex = new Regex(Regex.Escape(query), RegexOptions.IgnoreCase);
        return regex.Replace(text, match => $"\u001b[0;43m{match.Value}\u001b[0;0m");
    }
}