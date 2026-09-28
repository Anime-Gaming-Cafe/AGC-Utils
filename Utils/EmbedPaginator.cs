using DisCatSharp;
using DisCatSharp.Entities;
using DisCatSharp.Interactivity.Extensions;
using DisCatSharp.Interactivity.Enums;

namespace AGC_Management.Utils;

public static class EmbedPaginator
{
    private const string PrevButtonId = "embedpager_prev";
    private const string NextButtonId = "embedpager_next";

    public static async Task SendPaginatedEmbed(CommandContext ctx, string title, string description,
        DiscordColor color, string thumbnailUrl = null, string footerText = null, string footerIcon = null)
    {
        var pages = BuildPages(title, description, color, thumbnailUrl, footerText, footerIcon)
            .Select(embed => new DisCatSharp.Interactivity.Entities.Page().WithEmbed(new DiscordEmbedBuilder(embed)))
            .ToList();

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

    /// <summary>
    ///     Replaces the content of an already sent message (e.g. a loading placeholder) with the paginated
    ///     embed. Returns once the first page is shown; page navigation runs in the background.
    /// </summary>
    public static async Task ShowPaginatedEmbed(DiscordClient client, DiscordMessage message, DiscordUser user,
        string title, string description, DiscordColor color, string thumbnailUrl = null, string footerText = null,
        string footerIcon = null)
    {
        var pages = BuildPages(title, description, color, thumbnailUrl, footerText, footerIcon);
        if (pages.Count == 1)
        {
            await message.ModifyAsync(new DiscordMessageBuilder().AddEmbed(pages[0]));
            return;
        }

        await message.ModifyAsync(new DiscordMessageBuilder().AddEmbed(pages[0])
            .AddComponents(NavigationButtons(0, pages.Count, true)));

        _ = Task.Run(async () =>
        {
            var index = 0;
            try
            {
                var interactivity = client.GetInteractivity();
                while (true)
                {
                    var result = await interactivity.WaitForButtonAsync(message, user, (TimeSpan?)null);
                    if (result.TimedOut) break;

                    index = result.Result.Id == NextButtonId
                        ? Math.Min(index + 1, pages.Count - 1)
                        : Math.Max(index - 1, 0);
                    await result.Result.Interaction.CreateResponseAsync(InteractionResponseType.UpdateMessage,
                        new DiscordInteractionResponseBuilder().AddEmbed(pages[index])
                            .AddComponents(NavigationButtons(index, pages.Count, true)));
                }

                await message.ModifyAsync(new DiscordMessageBuilder().AddEmbed(pages[index])
                    .AddComponents(NavigationButtons(index, pages.Count, false)));
            }
            catch (Exception e)
            {
                CurrentApplication.Logger.Warning(e, "EmbedPaginator: pagination of message {MessageId} failed",
                    message.Id);
            }
        });
    }

    private static DiscordButtonComponent[] NavigationButtons(int index, int count, bool active)
    {
        return
        [
            new DiscordButtonComponent(ButtonStyle.Secondary, PrevButtonId, "◀", !active || index == 0),
            new DiscordButtonComponent(ButtonStyle.Secondary, NextButtonId, "▶", !active || index == count - 1)
        ];
    }

    private static List<DiscordEmbed> BuildPages(string title, string description, DiscordColor color,
        string thumbnailUrl, string footerText, string footerIcon)
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
        return chunks.Select((chunk, i) =>
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

            return embed.Build();
        }).ToList();
    }
}
