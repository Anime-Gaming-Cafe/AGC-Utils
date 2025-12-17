using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AGC_Management.Attributes;
using AGC_Management.Utils;
using DisCatSharp.Interactivity;
using DisCatSharp.Interactivity.Enums;
using DisCatSharp.Interactivity.Extensions;

namespace AGC_Management.Commands;

public class TicketSucheCommand : BaseCommandModule
{
    private static readonly string BaseUrl = "https://ticketsystem.animegamingcafe.de/transcripts/";

    [Command("ticketsuche")]
    [RequireStaffRole]
    [Aliases("ticketsearch")]
    public async Task TicketSuche(CommandContext ctx, [RemainingText] string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            await ctx.RespondAsync("Bitte gebe eine Query an!");
            return;
        }

        if (!TicketSearchTools.ScanDone)
        {
            await ctx.RespondAsync("Tickets werden noch gescannt, bitte warte einen Moment!");
            return;
        }

        await ctx.Message.CreateReactionAsync(DiscordEmoji.FromGuildEmote(ctx.Client, 1084157150747697203));
        var results = TicketSearchTools.SearchTicketsByQuery(query);
        if (results.Count == 0)
        {
            await ctx.RespondAsync("Keine Tickets mit dieser Query gefunden!");
            await ctx.Message.DeleteAllReactionsAsync();
            return;
        }

        var pages = new List<Page>();

        var entries = results.Select(result =>
        {
            var htmlresult = result["snippet"];
            htmlresult = htmlresult.Replace("\n", " ");
            htmlresult = WebUtility.HtmlDecode(Regex.Replace(htmlresult, "<.*?>", string.Empty));
            htmlresult = htmlresult.Replace("<br>", " ");

            var stringbuilder = new StringBuilder();
            stringbuilder.AppendLine($"**{result["title"]}**");
            stringbuilder.AppendLine($"```ansi\n" +
                                     $"{htmlresult}\n```");
            stringbuilder.AppendLine($"[Vollständig Anzeigen]({BaseUrl + result["fileName"]})");
            stringbuilder.AppendLine();
            return stringbuilder.ToString();
        }).ToList();

        var totalEntries = entries.Count;
        var pageSize = 5;
        var pageCount = (totalEntries + pageSize - 1) / pageSize;

        for (var i = 0; i < totalEntries; i += pageSize)
        {
            var page = new Page
            {
                Embed = new DiscordEmbedBuilder
                {
                    Title = "Ticket Suche",
                    Description = string.Join("\n", entries.Skip(i).Take(pageSize)),
                    Color = DiscordColor.Green,
                    Footer = new DiscordEmbedBuilder.EmbedFooter
                    {
                        Text = $"Seite {i / pageSize + 1}/{pageCount}"
                    }
                }
            };
            pages.Add(page);
        }

        await ctx.Message.DeleteAllReactionsAsync();
        await ctx.Client.GetInteractivity()
            .SendPaginatedMessageAsync(ctx.Channel, ctx.User, pages, PaginationBehaviour.Ignore);
    }
}