using DisCatSharp;
using DisCatSharp.Entities;
using DisCatSharp.Exceptions;
using DisCatSharp.Interactivity.Extensions;
using DisCatSharp.Interactivity.Enums;

public static class EmbedPaginator
{
    public static async Task SendPaginatedEmbed(CommandContext ctx, string title, string description, DiscordColor color, string thumbnailUrl = null, string footerText = null, string footerIcon = null)
    {
        const int MAX_LENGTH = 4000;
        var chunks = new List<string>();
        for (int i = 0; i < description.Length; i += MAX_LENGTH)
        {
            chunks.Add(description.Substring(i, Math.Min(MAX_LENGTH, description.Length - i)));
        }

        var embeds = chunks.Select((chunk, i) =>
        {
            var embed = new DiscordEmbedBuilder()
            .WithTitle(title)
            .WithDescription(chunk)
            .WithColor(color);

            if (!string.IsNullOrEmpty(thumbnailUrl))
                embed.WithThumbnail(thumbnailUrl);

            if (!string.IsNullOrEmpty(footerText))
                embed.WithFooter($"{footerText} | Seite {i + 1}/{chunks.Count}", footerIcon);

            return embed;
        }).ToList();

        var pages = embeds.Select(e => new DisCatSharp.Interactivity.Page(embed: e)).ToList();

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