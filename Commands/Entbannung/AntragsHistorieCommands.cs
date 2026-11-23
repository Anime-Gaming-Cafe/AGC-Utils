#region

using AGC_Management.Attributes;
using AGC_Management.Entities.Entbannung;
using AGC_Management.Services;
using DisCatSharp.ApplicationCommands;
using DisCatSharp.ApplicationCommands.Attributes;
using DisCatSharp.ApplicationCommands.Context;

#endregion

namespace AGC_Management.Commands.Entbannung;

[ApplicationCommandRequireUnbanTeam]
[SlashCommandGroup("antragshistorie", "Antrangshistorie Befehle")]
public sealed class AntragsHistorieCommands : ApplicationCommandsModule
{
    [SlashCommand("eintragen", "Fügt einen Antrag zur Historie hinzu.")]
    public static async Task AddAntrag(InteractionContext ctx,
        [Option("Antragsstatus", "Der Status des Antrages (Angenommen, Abgelehnt)")]
        [Choice("Angenommen", "Angenommen")]
        [Choice("Abgelehnt", "Abgelehnt")]
        string status,
        [Option("Antragsnummer", "Die Antragsnummer")]
        string antragsnummer,
        [Option("User", "Der User, der den Antrag gestellt hat")]
        DiscordUser user,
        [Option("Antragsgrund", "Der Grund für die Entscheidung")]
        string grund)
    {
        await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().AsEphemeral());

        await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent("Prüfe Eingaben..."));
        await Task.Delay(1000);

        if (!antragsnummer.All(char.IsDigit))
        {
            await ctx.EditResponseAsync(
                new DiscordWebhookBuilder().WithContent("⚠️ Die Antragsnummer ist ungültig!"));
            return;
        }

        if (await EntbannungService.AntragExistsAsync(antragsnummer))
        {
            await ctx.EditResponseAsync(
                new DiscordWebhookBuilder().WithContent("⚠️ Die Antragsnummer existiert bereits!"));
            return;
        }

        await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent("Schreibe in die Datenbank..."));
        await Task.Delay(1000);

        var entbannt = status == "Angenommen";
        await EntbannungService.AddAntragAsync(new AntragsverlaufEntry(user.Id, antragsnummer, entbannt, grund,
            ctx.User.Id, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));

        await ctx.EditResponseAsync(
            new DiscordWebhookBuilder().WithContent("✅ Antrag wurde erfolgreich eingetragen!"));

        await EntbannungService.PostHistoryEmbedAsync(ctx.Client, entbannt, ctx.User, antragsnummer, user, grund);
    }

    [SlashCommand("recent", "Zeigt die letzten 5 Anträge an.")]
    public static async Task RecentAntraege(InteractionContext ctx)
    {
        await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().AsEphemeral());

        await ctx.EditResponseAsync(
            new DiscordWebhookBuilder().WithContent("Lade Daten... <a:agcutils_loading:952604537515024514>"));
        await Task.Delay(1000);

        var recent = await EntbannungService.GetRecentAntraegeAsync(5);

        var eb = new DiscordEmbedBuilder().WithColor(DiscordColor.Blurple);
        var pos = 0;
        var neg = 0;

        foreach (var antrag in recent)
        {
            if (antrag.Entbannt) pos++;
            else neg++;

            eb.AddField(new DiscordEmbedField($"Antragsnummer: {antrag.AntragsId}",
                $"> User: <@{antrag.UserId}> ({antrag.UserId})\n" +
                $"> Entbannung: {EntbannungService.BoolToEmoji(antrag.Entbannt)}\n" +
                $"> Grund: ``{antrag.Reason}``\n" +
                $"> Bearbeitet von: <@{antrag.ModId}> ({antrag.ModId})\n" +
                $"> Zeitpunkt: <t:{antrag.Timestamp}:f> (<t:{antrag.Timestamp}:R>)"));
            await Task.Delay(100);
        }

        eb.WithTitle($"Letzte {recent.Count} Anträge");
        eb.WithFooter($"Entbannt: {pos} | Nicht Entbannt: {neg}");

        await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(eb));
    }
}
