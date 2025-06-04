#region

using AGC_Management.Utils;

#endregion

namespace AGC_Management.Managers;

public class SnippetManager
{
}

[EventHandler]
public class SnippetListener
{
    private readonly long catid = long.Parse(BotConfig.GetConfig()["TicketConfig"]["SupportCategoryId"]);
    private readonly long teamroleid = long.Parse(BotConfig.GetConfig()["TicketConfig"]["TeamRoleId"]);

    [Event]
    public async Task MessageCreated(DiscordClient client, MessageCreateEventArgs e)
    {
        _ = Task.Run(async () =>
        {
            if (e.Guild == null) return;

            if (e.Message.Author.IsBot) return;

            if (e.Message.Channel.Parent == null) return;

            if ((long)e.Message.Channel.Parent.Id != catid) return;

            var openticket = await TicketManagerHelper.IsOpenTicket(e.Message.Channel);
            if (!openticket) return;

            var sup = TeamChecker.IsSupporter(await e.Message.Author.ConvertToMember(e.Message.Guild));
            if (!sup) return;

            var string_to_search = e.Message.Content;
            if (string.IsNullOrEmpty(string_to_search)) return;

            var snipped = await SnippetManagerHelper.GetSnippetAsync(string_to_search);
            if (snipped != null && !e.Message.Author.IsBot)
            {
                snipped = SnippetManagerHelper.FormatStringWithVariables(snipped);
                await e.Message.DeleteAsync(snipped);
                var eb = new DiscordEmbedBuilder()
                    .WithDescription(snipped)
                    .WithColor(DiscordColor.Gold)
                    .WithTitle("Hinweis").WithFooter("AGC Support-System", e.Message.Guild.IconUrl);
                var users_in_ticket = await TicketManagerHelper.GetTicketUsers(e.Message.Channel);
                var ping = "";
                foreach (var user in users_in_ticket) ping = ping + $" {user.Mention}";

                DiscordMessageBuilder mb = new();
                mb.WithContent(ping).AddEmbed(eb);
                await e.Message.Channel.SendMessageAsync(mb);
            }
        });
    }
}