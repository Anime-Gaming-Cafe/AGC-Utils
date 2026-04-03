using DisCatSharp;
using DisCatSharp.Entities;
using DisCatSharp.Interactivity.Extensions;
using DisCatSharp.Interactivity.Enums;

namespace AGC_Management.Utils;

public static class EmbedPaginator
{
    public static async Task SendPaginatedEmbed(CommandContext ctx, string title, string description,
        DiscordColor color, string thumbnailUrl = null, string footerText = null, string footerIcon = null)
    {
        const int MAX_LENGTH = 4000;
        description ??= string.Empty;

        var chunks = new List<string>();
        var current = new System.Text.StringBuilder();

        foreach (var line in description.Split('\n'))
        {
            // Wenn die aktuelle Zeile den Chunk überlaufen würde, Chunk abschließen
            if (current.Length > 0 && current.Length + line.Length + 1 > MAX_LENGTH)
            {
                chunks.Add(current.ToString().TrimEnd('\n'));
                current.Clear();
            }

            // Einzelne Zeilen die selbst zu lang sind, hart aufteilen
            if (line.Length > MAX_LENGTH)
            {
                var rest = line;
                while (rest.Length > MAX_LENGTH)
                {
                    chunks.Add(rest[..MAX_LENGTH]);
                    rest = rest[MAX_LENGTH..];
                }
                current.Append(rest).Append('\n');
            }
            else
            {
                current.Append(line).Append('\n');
            }
        }

        if (current.Length > 0)
            chunks.Add(current.ToString().TrimEnd('\n'));

        if (chunks.Count == 0)
            chunks.Add(string.Empty);

        var totalPages = chunks.Count;
        var pages = chunks.Select((chunk, i) =>
        {
            var embed = new DiscordEmbedBuilder()
                .WithTitle(title)
                .WithDescription(chunk)
                .WithColor(color);

            if (!string.IsNullOrEmpty(thumbnailUrl))
                embed.WithThumbnail(thumbnailUrl);

            var footer = totalPages > 1 && !string.IsNullOrEmpty(footerText)
                ? $"{footerText} | Seite {i + 1}/{totalPages}"
                : footerText;

            if (!string.IsNullOrEmpty(footer))
                embed.WithFooter(footer, footerIcon);

            return new DisCatSharp.Interactivity.Page(embed: embed);
        }).ToList();

        if (pages.Count == 1)
        {
            await ctx.RespondAsync(pages[0].Embed);
            return;
        }

        var interactivity = ctx.Client.GetInteractivity();
        await interactivity.SendPaginatedMessageAsync(
            ctx.Channel,
            ctx.User,
            pages,
            PaginationBehaviour.Ignore,
            ButtonPaginationBehavior.Disable,
            CancellationToken.None
        );
    }
}
