#region

using AGC_Management.Services;
using AGC_Management.Utils;

#endregion

namespace AGC_Management.Eventlistener.Entbannung;

[EventHandler]
public class AntragKeywordListener : BaseCommandModule
{
    private static readonly HashSet<string> MoveKeywords =
        ["accept", "acceptsafety", "deny", "deny2", "deny13", "deny24", "bannsystem", "noai2"];

    [Event]
    public static Task MessageCreated(DiscordClient client, MessageCreateEventArgs e)
    {
        if (!EntbannungConfig.IsUnbanGuild(e.Guild)) return Task.CompletedTask;
        if (!e.Channel.Name.StartsWith("antrag-") || e.Channel.Name.EndsWith("-geschlossen"))
            return Task.CompletedTask;
        if (e.Message.Author.IsBot) return Task.CompletedTask;

        var content = e.Message.Content?.Trim().ToLower() ?? string.Empty;
        if (!MoveKeywords.Contains(content)) return Task.CompletedTask;

        _ = Task.Run(async () =>
        {
            try
            {
                var member = await e.Guild.GetMemberAsync(e.Message.Author.Id);
                if (member.Roles.All(r => r.Id != EntbannungConfig.TeamRoleId)) return;

                if (content is "accept" or "acceptsafety")
                    await RecordAcceptAsync(client, e, content);

                var bearbeitetCategoryId = EntbannungConfig.BearbeitetCategoryId;
                if (e.Channel.ParentId == bearbeitetCategoryId) return;
                await e.Channel.ModifyAsync(x => x.Parent = e.Guild.GetChannel(bearbeitetCategoryId));
            }
            catch (Exception ex)
            {
                CurrentApplication.Logger.Error(ex, "Entbannung: Keyword-Listener fehlgeschlagen");
            }
        });
        return Task.CompletedTask;
    }

    private static async Task RecordAcceptAsync(DiscordClient client, MessageCreateEventArgs e, string content)
    {
        try
        {
            var antragsnummer = e.Channel.Name.Split('-').LastOrDefault();
            if (string.IsNullOrEmpty(antragsnummer) || !antragsnummer.All(char.IsDigit)) return;

            var userId = await EntbannungService.GetTicketUserIdAsync(e.Channel);
            if (userId is null) return;

            var targetUser = await client.GetUserAsync(userId.Value);
            var reason = content == "acceptsafety"
                ? "Account gehackt - entbannt nach Antrag"
                : "Antrag positiv abgestimmt";
            await EntbannungService.TryAddAntragsverlaufAsync(client, true, e.Message.Author, antragsnummer,
                targetUser, reason);
        }
        catch (Exception ex)
        {
            CurrentApplication.Logger.Error(ex, "Entbannung: Antragshistorie nach accept/acceptsafety fehlgeschlagen");
        }
    }
}
