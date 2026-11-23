#region

using AGC_Management.Services;
using AGC_Management.Utils;
using DisCatSharp.ApplicationCommands;
using DisCatSharp.ApplicationCommands.Attributes;
using DisCatSharp.ApplicationCommands.Context;

#endregion

namespace AGC_Management.Commands.Entbannung;

public sealed class EntbannungPanelCommands : ApplicationCommandsModule
{
    [ApplicationCommandRequirePermissions(Permissions.Administrator)]
    [SlashCommand("sendpanel", "Sendet das Panel für den Entbannungsantrag.")]
    public static async Task SendNotePanel(InteractionContext ctx)
    {
        await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().WithContent("Panel gesendet!").AsEphemeral());
        var embed = new DiscordEmbedBuilder()
            .WithAuthor(ctx.Guild.Name, iconUrl: ctx.Guild.IconUrl)
            .WithDescription(
                "Hey! Willkommen auf dem Entbannungsserver vom ``Anime & Gaming Café``. Hier kannst du einen Entbannungsantrag stellen, wenn du auf dem Hauptserver gebannt wurdest. Drücke dazu einfach auf den Button unten und folge den Anweisungen. \n\n" +
                "📝 Bitte eröffne für alle Anliegen ein Ticket. Bitte sende keine Direktnachrichten oder FAs an Teammitglieder. Dies kann zu einer Ablehnung deines Antrags führen! \n\n⚠️**Bitte lies unsere Anforderungen für den Antrag genau durch. Ein nichtlesen der Hinweise führt zur __direkten Ablehnung__. (Ja, auch wenn du schonmal hier warst)**");
        var button = new DiscordButtonComponent(ButtonStyle.Primary, "open_appealticketinfo",
            "Entbannungsantrag erstellen", emoji: new DiscordComponentEmoji("📝"));
        await ctx.Channel.SendMessageAsync(new DiscordMessageBuilder().AddComponents(button).AddEmbed(embed));
    }

    [ApplicationCommandRequirePermissions(Permissions.Administrator)]
    [SlashCommand("sendsperrepanel", "Sendet das Sperre Panel für den Entbannungsantrag.")]
    public static async Task SendSperrePanel(InteractionContext ctx)
    {
        await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().WithContent("Panel gesendet!").AsEphemeral());
        var embed = new DiscordEmbedBuilder()
            .WithTitle("Antragssperre!")
            .WithColor(DiscordColor.Red)
            .WithDescription(
                "Du bist derzeit für Anträge wegen deinem letzten Antrag gesperrt. Du kannst erst wieder einen Antrag stellen, wenn deine Sperre abgelaufen ist. Bitte kontaktiere in der Zwischenzeit kein Teammitglied. \n\n" +
                "📝 Du kannst hier nachschauen, wie lange deine Sperre noch anhält.");
        var button = new DiscordButtonComponent(ButtonStyle.Primary, "open_sperrinfo",
            "Wie lange bin ich gesperrt?", emoji: new DiscordComponentEmoji("📝"));
        await ctx.Channel.SendMessageAsync(new DiscordMessageBuilder().AddComponents(button).AddEmbed(embed));
    }

    [ApplicationCommandRequirePermissions(Permissions.Administrator)]
    [SlashCommand("votestats", "Zeigt die Abstimmungsstatistiken an.")]
    public static async Task VoteStats(InteractionContext ctx,
        [Option("antragskanal", "Der Kanal, in dem der Antrag gestellt wurde.")]
        DiscordChannel channel)
    {
        var abstimmung = await EntbannungService.GetAbstimmungByChannelAsync(channel.Id);
        if (abstimmung is null)
        {
            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().WithContent("Dieser Kanal ist nicht in einer Abstimmung!")
                    .AsEphemeral());
            return;
        }

        var votes = await EntbannungService.GetVotesAsync(abstimmung.MessageId);
        var positive = votes.Where(v => v.Vote == Entities.Entbannung.VoteType.Positive)
            .Select(v => $"<@{v.UserId}>").ToList();
        var negative = votes.Where(v => v.Vote == Entities.Entbannung.VoteType.Negative)
            .Select(v => $"<@{v.UserId}>").ToList();
        var expiresAt = abstimmung.ExpiresAt;
        var voteChannelId = EntbannungConfig.AbstimmungsChannelId;

        var voteEmbed = new DiscordEmbedBuilder()
            .WithTitle("Abstimmungsstatistiken")
            .WithDescription($"Abstimmung für den Antrag ``{channel.Name}`` | ({channel.Mention})\n" +
                             $"**Positive Stimmen:** {abstimmung.PositiveVotes}\n" +
                             "**User, die Positiv abgestimmt haben:**\n" +
                             $"{(positive.Count > 0 ? string.Join(", ", positive) : "Keine positiven Stimmen")}\n\n" +
                             $"**Negative Stimmen:** {abstimmung.NegativeVotes}\n" +
                             "**User, die Negativ abgestimmt haben:**\n" +
                             $"{(negative.Count > 0 ? string.Join(", ", negative) : "Keine negativen Stimmen")}\n\n" +
                             $"**Abstimmung erstellt von:** {(abstimmung.CreatedBy == 0 ? "Unbekannt" : $"<@{abstimmung.CreatedBy}>")}\n" +
                             $"**Abstimmung ID:** {EntbannungService.VoteId(abstimmung.MessageId)}\n" +
                             $"Die Abstimmung ist noch nicht beendet. Endet: <t:{expiresAt}:f> (<t:{expiresAt}:R>)")
            .WithColor(EntbannungService.GetVoteColor(abstimmung.PositiveVotes, abstimmung.NegativeVotes) switch
            {
                0 => DiscordColor.Red,
                1 => DiscordColor.Green,
                2 => DiscordColor.Yellow,
                _ => DiscordColor.Gray
            });

        var antragsnummer = channel.Name.Split('-').LastOrDefault()?.Trim();
        var jumpToVoteButton = new DiscordLinkButtonComponent(
            $"https://discord.com/channels/{ctx.Guild.Id}/{voteChannelId}/{abstimmung.MessageId}",
            "Zur Abstimmung springen");
        var jumpToAntragButton = new DiscordLinkButtonComponent(
            $"https://discord.com/channels/{ctx.Guild.Id}/{channel.Id}", $"Zum Antrag {antragsnummer} springen");
        await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder()
                .AddEmbed(voteEmbed.Build())
                .AddComponents(jumpToAntragButton, jumpToVoteButton)
                .AsEphemeral());
    }
}
