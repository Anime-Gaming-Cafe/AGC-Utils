#region

using AGC_Management.Services;
using AGC_Management.Utils;

#endregion

namespace AGC_Management.Managers;

public class SnippetManager
{
}

[EventHandler]
public class SnippetListener
{
    [Event]
    public async Task MessageCreated(DiscordClient client, MessageCreateEventArgs e)
    {
        _ = Task.Run(async () =>
        {
            if (e.Guild == null) return;

            if (e.Message.Author.IsBot) return;

            if (e.Message.Channel.Parent == null) return;

            var ticketParents = await TicketCategoryService.GetTicketParentIdsAsync();
            if (!ticketParents.Contains(e.Message.Channel.Parent.Id)) return;

            var openticket = await TicketManagerHelper.IsOpenTicket(e.Message.Channel);
            if (!openticket) return;

            var sup = await TicketAccess.MayHandleAsync(await e.Message.Author.ConvertToMember(e.Message.Guild),
                e.Message.Channel);
            if (!sup) return;

            var string_to_search = e.Message.Content;
            if (string.IsNullOrEmpty(string_to_search)) return;

            var snipped = await SnippetManagerHelper.GetSnippetAsync(string_to_search);
            if (snipped != null && !e.Message.Author.IsBot)
            {
                snipped = await SnippetTemplateResolver.ResolveAsync(snipped, e.Message.Channel, e.Message.Author);
                await e.Message.DeleteAsync(snipped);
                var eb = new DiscordEmbedBuilder()
                    .WithDescription(snipped)
                    .WithColor(DiscordColor.Gold)
                    .WithTitle("Hinweis").WithFooter("AGC Support-System", e.Message.Guild.IconUrl);
                var users_in_ticket = await TicketManagerHelper.GetTicketUsers(e.Message.Channel);
                var ping = "";
                foreach (var user in users_in_ticket) ping += $" {user.Mention}";

                DiscordMessageBuilder mb = new();
                mb.WithContent(ping).AddEmbed(eb);
                await e.Message.Channel.SendMessageAsync(mb);
            }
        });
    }
}