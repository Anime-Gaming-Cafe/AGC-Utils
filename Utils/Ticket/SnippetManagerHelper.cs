#region

using System.Text.RegularExpressions;
using AGC_Management.Managers;

#endregion

namespace AGC_Management.Helper;

public class SnippetManagerHelper
{
    public static async Task<string?> GetSnippetAsync(string snippetId)
    {
        if (string.IsNullOrEmpty(snippetId)) return null;

        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = con.CreateCommand("SELECT snipped_text FROM snippets WHERE snip_id = @snippetId");
        cmd.Parameters.AddWithValue("@snippetId", snippetId);

        await using var reader = await cmd.ExecuteReaderAsync();

        while (reader.Read()) return reader.GetString(0);

        await reader.CloseAsync();

        return null;
    }

    public static async Task SendSnippetAsync(DiscordInteraction e)
    {
        var irb = new DiscordInteractionResponseBuilder();
        irb.WithContent("Sende snippet...");
        await e.CreateResponseAsync(InteractionResponseType.UpdateMessage, irb);
        var snipId = e.Data.Values[0];
        var snippet = await GetSnippetAsync(snipId);
        if (string.IsNullOrEmpty(snippet)) return;

        snippet = FormatStringWithVariables(snippet);
        var eb = new DiscordEmbedBuilder()
            .WithDescription(snippet)
            .WithColor(DiscordColor.Gold)
            .WithTitle("Hinweis").WithFooter("AGC Support-System", e.Guild.IconUrl);
        var users_in_ticket = await TicketManagerHelper.GetTicketUsers(e.Channel);
        var ping = "";
        foreach (var user in users_in_ticket) ping += $" {user.Mention}";

        DiscordMessageBuilder mb = new();
        mb.WithContent(ping).AddEmbed(eb);
        await e.Channel.SendMessageAsync(mb);
        var nrb = new DiscordWebhookBuilder();
        nrb.WithContent("Snippet gesendet!");
        await e.EditOriginalResponseAsync(nrb);
    }

    public static string FormatStringWithVariables(string inputString)
    {
        var unixTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var snippetString = inputString;

        if (snippetString.Contains("{unixtimestamp"))
        {
            var matches = Regex.Matches(snippetString, @"{unixtimestamp(?:\+([0-9]+))?}");
            foreach (Match match in matches)
                if (match.Groups.Count == 2)
                {
                    var add = match.Groups[1].Value;
                    if (string.IsNullOrEmpty(add))
                        snippetString = snippetString.Replace(match.Value, unixTimestamp.ToString());
                    else
                        snippetString = snippetString.Replace(match.Value,
                            (unixTimestamp + Convert.ToInt64(add)).ToString());
                }
        }

        return snippetString;
    }


    public static async Task<List<(string snipId, string snippedText)>> GetAllSnippetsAsync()
    {
        var resultList = new List<(string, string)>();

        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();


        await using var cmd = con.CreateCommand("SELECT snip_id, snipped_text FROM snippets");

        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync()) resultList.Add((reader.GetString(0), reader.GetString(1)));

        await reader.CloseAsync();

        return resultList;
    }
}