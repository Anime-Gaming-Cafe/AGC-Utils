namespace AGC_Management.Utils;

public static class EntbannungMessages
{
    public static List<DiscordEmbed> UnbanNotes()
    {
        var color = new DiscordColor("2f3136");
        var embed1 = new DiscordEmbedBuilder()
            .WithAuthor("Aufmerksam lesen!")
            .WithTitle("Warum wurde ich gebannt?")
            .WithDescription("Du wurdest gebannt, weil du auf AGC gegen die Regeln verstoßen hast. \n" +
                             "Sofern du nicht weißt, wieso du gebannt wurdest, erfrage bitte dies gleich zum Anfang deines Entbannungsantrags. Wir geben dir dann Auskunft darüber, wieso du gebannt wurdest. \n\n" +
                             "- Die Erfragung des Grundes fließt nicht in die Bewertung deines Entbannungsantrags ein. \n\n" +
                             "> ⚠️ Wenn du denkst, dass du zu unrecht gebannt wurdest, teile uns dies bitte __zu Beginn__ deines Antrags mit!")
            .WithColor(color);
        var embed2 = new DiscordEmbedBuilder()
            .WithTitle("Antragsstellung")
            .WithDescription(
                "Es wird von dir erwartet, dass du dich umfassend zu deinem Bann äußerst. Außerdem erwarten wir, dass du folgende Fragen ausführlich beantwortest: \n" +
                "- Wieso wurdest du gebannt bzw hast gegen unsere Serverregeln verstoßen? \n" +
                "- Was hast du daraus gelernt? \n" +
                "- Was wirst du anders machen, um so eine Situation in Zukunft zu vermeiden? \n" +
                "- Wie können wir sicher sein, dass du dich in Zukunft an unsere Regeln hältst? \n")
            .WithFooter(
                "Das herrunterrattern als Stichpunkte führt möglicherweise zu einer Ablehnung! Antworte so ausführlich wie möglich und in einem Fließtext!")
            .WithColor(color);
        var embed3 = new DiscordEmbedBuilder()
            .WithTitle("Wichtig!")
            .WithDescription(
                "Unser Team achtet auf eine angemessene Ausdrucksweise. Achte auch so gut wie möglich auf Rechtschreibung und Grammatik. \n" +
                "Verzichte bitte auf mögliche Lückenfüller, indem du auf unnötige Rechtfertigungen verzichtest, wie z.B. warum du entbannt werden möchtest. \n\n" +
                "> Solltest du Schwierigkeiten mit der Rechtschreibung haben, durch z.b eine Lese- und Rechtschreibschwäche, so kannst du dies gerne in deinem Antrag erwähnen. \n\n" +
                "Nimm dir außerdem Zeit. Wir möchten sehen, dass dir etwas an der Entbannung liegt. Schau also dass du alle Kriterien erfüllst. \n\n" +
                "⚠️ Wenn der Entbannungsantrag nicht ordentlich bearbeitet wird, wird er abgelehnt! \n\n" +
                "⚠️ Das verwenden einer AI/KI kann zu einer direkten Ablehnung führen. \n\n" +
                "🛑 Sollte dein Antrag abgelehnt werden, wirst du für 3 Monate gesperrt. \n\n" +
                "🕰️ Du hast für den Entbannungsantrag 24 Stunden Zeit. Sollte in dieser Zeit kein Antrag gestellt werden, wirst du ebenfalls abgelehnt und für 3 Monate gesperrt.\n\n" +
                "⌚ **Das nichtlesen der Anforderungen kann zu einer __direkten Ablehnung__ führen. Daher bitten wir dich __in jedem fall__, die Anforderungen sorgfältig zu lesen.**")
            .WithColor(color);
        return [embed1.Build(), embed2.Build(), embed3.Build()];
    }

    public static DiscordEmbed VoteRunning(DiscordChannel antragChannel, long expiresAt, int negativeVotes,
        int positiveVotes, int resultForColor, int teamMemberCount)
    {
        return new DiscordEmbedBuilder()
            .WithTitle("Abstimmung läuft!")
            .WithDescription(
                $"Die Abstimmung für den Antrag ``{antragChannel.Name}`` | ({antragChannel.Mention}) steht bereit!\n" +
                VoteStatsLines(negativeVotes, positiveVotes, teamMemberCount) +
                $"Die Abstimmung läuft bis <t:{expiresAt}:f> (<t:{expiresAt}:R>)\n\n" +
                "-# Die Anzahl der Stimmen wird alle 5 Minuten aktualisiert.\n")
            .WithColor(VoteColor(resultForColor))
            .Build();
    }

    public static DiscordEmbed VoteFinished(DiscordChannel antragChannel, long endedAt, int negativeVotes,
        int positiveVotes, int resultForColor, int teamMemberCount)
    {
        return new DiscordEmbedBuilder()
            .WithTitle("Abstimmung beendet!")
            .WithDescription(
                $"Die Abstimmung für den Antrag ``{antragChannel.Name}`` | ({antragChannel.Mention}) ist beendet!\n" +
                VoteStatsLines(negativeVotes, positiveVotes, teamMemberCount) +
                $"Die Abstimmung endete am <t:{endedAt}:f> (<t:{endedAt}:R>)\n\n")
            .WithColor(VoteColor(resultForColor))
            .Build();
    }

    public static List<DiscordButtonComponent> VoteButtons(ulong antragChannelId)
    {
        return
        [
            new DiscordButtonComponent(ButtonStyle.Secondary, "vote_yes_" + antragChannelId, "👍 Ja"),
            new DiscordButtonComponent(ButtonStyle.Secondary, "vote_no_" + antragChannelId, "👎 Nein")
        ];
    }

    private static string VoteStatsLines(int negativeVotes, int positiveVotes, int teamMemberCount)
    {
        var totalVotes = positiveVotes + negativeVotes;
        var votePercentage = teamMemberCount > 0 ? (double)totalVotes / teamMemberCount * 100 : 0;
        return $"**Positive Stimmen:** {positiveVotes}\n" +
               $"**Negative Stimmen:** {negativeVotes}\n" +
               $"**Abstimmungsbeteiligung:** {votePercentage:F1}% ({totalVotes}/{teamMemberCount})\n";
    }

    private static DiscordColor VoteColor(int resultForColor)
    {
        return resultForColor switch
        {
            0 => DiscordColor.Red,
            1 => DiscordColor.Green,
            2 => DiscordColor.Yellow,
            _ => DiscordColor.Gray
        };
    }
}
