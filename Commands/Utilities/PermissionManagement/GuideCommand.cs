#region

using AGC_Management.Attributes;
using AGC_Management.Utils;
using DisCatSharp.ApplicationCommands.Attributes;
using DisCatSharp.ApplicationCommands.Context;
using DisCatSharp.Interactivity.Entities;
using DisCatSharp.Interactivity.Enums;
using DisCatSharp.Interactivity.Extensions;

#endregion

namespace AGC_Management.Commands.PermissionManagement;

public partial class PermissionManagement
{
    private static readonly (string Title, string Body)[] GuideChapters =
    [
        ("Extra Permissions in einem Satz",
            "Eine **Extra Permission** vergibt eine Rolle automatisch, sobald ein Mitglied die hinterlegten " +
            "Bedingungen erfüllt.\n\n" +
            "Geprüft wird, sobald jemand dem Server beitritt, eine Rolle bekommt oder verliert, den Boost " +
            "ändert oder einen Sprachkanal betritt oder verlässt. Zusätzlich läuft im Hintergrund " +
            "regelmäßig ein Abgleich über alle Mitglieder.\n\n" +
            "Jede Permission besteht aus fünf Teilen:\n" +
            "> **name** interner Schlüssel, z.B. ``media``\n" +
            "> **role** die Rolle die vergeben wird\n" +
            "> **Bedingungen** wann die Rolle fällig ist\n" +
            "> **mode** dauerhaft geprüft oder einmalig\n" +
            "> **auto-revoke** ob die Rolle wieder entzogen wird\n\n" +
            "Blättere mit den Buttons weiter. Ab Seite 9 stehen fertige Szenarien zum Nachbauen."),

        ("Bedingungsarten",
            "**Mit Zahlenwert** (brauchen ``value``, unterstützen ``comparator``)\n" +
            "> ``Level`` erreichtes Level\n" +
            "> ``MembershipAge`` Tage seit dem Join\n" +
            "> ``Messages`` gezählte Nachrichten\n" +
            "> ``VoiceMinutes`` gezählte Voice-Minuten\n\n" +
            "**Ohne Zahlenwert**\n" +
            "> ``Role`` besitzt eine bestimmte Rolle, braucht ``trigger-role``\n" +
            "> ``Boost`` boostet den Server\n" +
            "> ``Voice`` sitzt gerade in einem Sprachkanal\n" +
            "> ``Join`` ist auf dem Server, trifft immer zu\n\n" +
            "Nicht jede Option passt zu jeder Bedingung:\n" +
            "> ``scope-channel`` und ``scope-ids`` nur bei **Voice**, **Messages**, **VoiceMinutes**\n" +
            "> ``window-days`` nur bei **Messages** und **VoiceMinutes**\n" +
            "> ``comparator`` nur bei den vier Zahlen-Bedingungen\n\n" +
            "Gibst du eine Option an, die die Bedingung nicht unterstützt, lehnt der Befehl das mit einer " +
            "Meldung ab."),

        ("Gruppen: UND und ODER",
            "Das ist der Teil, der am häufigsten falsch verstanden wird.\n\n" +
            "> **Innerhalb einer Gruppe gilt ODER.** Eine einzige erfüllte Bedingung reicht.\n" +
            "> **Zwischen Gruppen gilt UND.** Jede Gruppe muss erfüllt sein.\n\n" +
            "Ohne Angabe landet alles in Gruppe ``1``. Solange du nur eine Gruppe benutzt, heißt das: " +
            "**irgendeine** deiner Bedingungen genügt.\n\n" +
            "Beispiel mit zwei Gruppen:\n" +
            "> Gruppe 1: ``Level >= 10`` oder ``Boost``\n" +
            "> Gruppe 2: ``MembershipAge >= 30``\n\n" +
            "Ergebnis: mindestens 30 Tage dabei **und** dazu entweder Level 10 oder Booster.\n\n" +
            "Willst du also ein echtes UND zwischen zwei Bedingungen, müssen sie in " +
            "**unterschiedliche Gruppen**."),

        ("negate und comparator",
            "**comparator** legt die Richtung einer Zahlen-Bedingung fest:\n" +
            "> ``Gte`` Wert ist **mindestens** so groß, das ist die Voreinstellung\n" +
            "> ``Lte`` Wert ist **höchstens** so groß\n\n" +
            "``Lte`` ist praktisch für Rollen, die nur für Neue gedacht sind, etwa " +
            "``MembershipAge Lte 7`` für eine Neuling-Rolle.\n\n" +
            "**negate** dreht genau eine Bedingung um. ``Boost`` mit ``negate:True`` trifft auf alle zu, " +
            "die **nicht** boosten.\n\n" +
            "negate wirkt immer nur auf die eine Bedingung, nie auf die ganze Gruppe."),

        ("Trigger-Modus",
            "**Recurring** ist die Voreinstellung. Die Bedingungen werden dauerhaft geprüft. Fallen sie " +
            "weg, entscheidet ``auto-revoke``, ob die Rolle wieder verschwindet.\n\n" +
            "**Once** vergibt die Rolle beim ersten Mal, an dem die Bedingungen zutreffen, und rührt sie " +
            "danach nie wieder an. Auch wenn die Bedingungen später wegfallen, bleibt die Rolle. " +
            "``auto-revoke`` ist bei Once wirkungslos.\n\n" +
            "Once eignet sich für Auszeichnungen, die man sich einmal verdient, etwa eine Veteranenrolle. " +
            "Recurring eignet sich für alles, was den aktuellen Status abbildet.\n\n" +
            "Dass ein Once-Trigger für ein Mitglied schon ausgelöst hat, wird gespeichert. Mit " +
            "``reset ... rearm-trigger:True`` machst du ihn wieder scharf."),

        ("Auto-Revoke",
            "Gilt nur im Modus **Recurring** und beantwortet die Frage: Bedingungen weg, Rolle auch weg?\n\n" +
            "> ``On`` Rolle wird entzogen\n" +
            "> ``Off`` Rolle bleibt liegen\n" +
            "> ``Inherit`` folgt der globalen Einstellung, das ist die Voreinstellung\n\n" +
            "Die globale Einstellung setzt du mit:\n" +
            "``/permissionmanagement settings auto-revoke:True``\n\n" +
            "Ab Werk steht sie auf **aus**. Solange du nichts umstellst, verhält sich ``Inherit`` also wie " +
            "``Off`` und einmal vergebene Rollen bleiben liegen."),

        ("Manuelle Overrides",
            "Ein Override schlägt die Automatik immer, egal was die Bedingungen sagen.\n\n" +
            "``/permissionmanagement grant member:@User permission:media reason:Sonderfall``\n" +
            "``/permissionmanagement revoke member:@User permission:media reason:Missbrauch``\n" +
            "``/permissionmanagement reset member:@User permission:media``\n\n" +
            "**reset** nimmt den Override zurück und übergibt das Mitglied wieder an die Automatik.\n\n" +
            "``duration`` macht den Override befristet. Danach fällt er von selbst auf Automatik zurück:\n" +
            "> ``7d`` sieben Tage, ``12h`` zwölf Stunden, ``1d12h`` anderthalb Tage\n" +
            "> Einheiten: ``s`` ``m`` ``h`` ``d`` ``w`` ``mo`` ``y``\n\n" +
            "``/permissionmanagement info member:@User`` zeigt für ein Mitglied alle Permissions, den " +
            "aktuellen Zustand und ob ein Override aktiv ist."),

        ("Szenario 1: Medien ab Level 5",
            "Das klassische Spam-Gate. Wer Level 5 erreicht, darf Bilder und Links posten.\n\n" +
            "```\n/permissionmanagement add-permission-role\n" +
            "  name: media\n  role: @Medien\n  trigger: Level\n  value: 5\n" +
            "  description: Medienrechte ab Level 5\n```\n" +
            "Fertig. Eine Bedingung, Gruppe 1, Modus Recurring.\n\n" +
            "Ob jemand die Rolle bei einem XP-Abzug wieder verliert, hängt an ``auto-revoke``. Soll sie " +
            "liegen bleiben, setze beim Anlegen ``auto-revoke: Off``."),

        ("Szenario 2: Booster ODER Level 20",
            "Eine Perk-Rolle für Booster, die aber auch durch Aktivität erreichbar sein soll.\n\n" +
            "Erst die Permission mit der ersten Bedingung:\n" +
            "```\n/permissionmanagement add-permission-role\n" +
            "  name: perks\n  role: @Perks\n  trigger: Boost\n```\n" +
            "Dann die zweite Bedingung in **dieselbe Gruppe**:\n" +
            "```\n/permissionmanagement add-condition\n" +
            "  permission: perks\n  type: Level\n  value: 20\n  group: 1\n```\n" +
            "Beide sitzen in Gruppe 1, also gilt ODER: Booster **oder** Level 20 genügt.\n\n" +
            "Hier lohnt ``auto-revoke: On``, damit die Rolle beim Ende des Boosts wieder weggeht, sofern " +
            "das Level nicht reicht."),

        ("Szenario 3: Aktive Schreiber",
            "Eine Rolle für alle, die im letzten Monat wirklich aktiv waren.\n\n" +
            "```\n/permissionmanagement add-permission-role\n" +
            "  name: aktiv\n  role: @Aktiv\n  trigger: Messages\n  value: 500\n" +
            "  window-days: 30\n  scope-channel: #general\n  auto-revoke: On\n```\n" +
            "``window-days: 30`` zählt nur die letzten 30 Tage, ist also ein rollendes Fenster und kein " +
            "Gesamtstand. ``scope-channel`` beschränkt die Zählung auf einen Kanal. Gibst du eine " +
            "Kategorie an, zählen alle Kanäle darin.\n\n" +
            "Mehrere Kanäle gehen über ``scope-ids`` als Liste mit Komma:\n" +
            "``scope-ids: 123456789012345678, 987654321098765432``\n\n" +
            "``auto-revoke: On`` ist hier wichtig, sonst behält die Rolle jeder, der einmal aktiv war."),

        ("Szenario 4: Rolle nur während Voice",
            "Eine Rolle, die nur existiert, solange jemand in einer bestimmten Kategorie im Voice sitzt, " +
            "etwa um einen Textkanal dazu freizuschalten.\n\n" +
            "```\n/permissionmanagement add-permission-role\n" +
            "  name: vcchat\n  role: @VC-Chat\n  trigger: Voice\n" +
            "  scope-channel: <Voice-Kategorie>\n  auto-revoke: On\n```\n" +
            "Beim Betreten und Verlassen eines Sprachkanals wertet der Bot sofort neu aus, die Rolle " +
            "kommt und geht also praktisch live.\n\n" +
            "Ohne ``auto-revoke: On`` würde die Rolle nach dem ersten Voice-Besuch für immer kleben " +
            "bleiben."),

        ("Szenario 5: Veteran, einmal und für immer",
            "Eine Auszeichnung für lange Mitgliedschaft, die niemand wieder verlieren soll.\n\n" +
            "```\n/permissionmanagement add-permission-role\n" +
            "  name: veteran\n  role: @Veteran\n  trigger: MembershipAge\n  value: 365\n" +
            "  mode: Once\n```\n" +
            "``mode: Once`` sorgt dafür, dass die Rolle nach dem ersten Auslösen nie wieder angefasst " +
            "wird.\n\n" +
            "Ein Sonderfall: Wer den Server verlässt und neu joint, startet bei ``MembershipAge`` wieder " +
            "bei null. Die Rolle bleibt trotzdem weg, weil der Once-Trigger als bereits ausgelöst " +
            "gespeichert ist. Willst du das für eine Person zurücksetzen:\n" +
            "``/permissionmanagement reset member:@User permission:veteran rearm-trigger:True``"),

        ("Szenario 6: Neulinge ausschließen",
            "Eine Rolle, die alle bekommen sollen, die **nicht** brandneu sind und **nicht** boosten, " +
            "etwa weil Booster bereits eine eigene Rolle haben.\n\n" +
            "```\n/permissionmanagement add-permission-role\n" +
            "  name: stamm\n  role: @Stammgast\n  trigger: MembershipAge\n  value: 14\n```\n" +
            "```\n/permissionmanagement add-condition\n" +
            "  permission: stamm\n  type: Boost\n  negate: True\n  group: 2\n```\n" +
            "Zwei Gruppen, also UND: mindestens 14 Tage dabei **und** kein Booster.\n\n" +
            "Hättest du ``group: 1`` genommen, wäre daraus ein ODER geworden und jeder Nicht-Booster " +
            "hätte die Rolle sofort bekommen. Genau hier geht es am häufigsten schief."),

        ("Alle Befehle",
            "**Anlegen und ändern**\n" +
            "> ``add-permission-role`` legt eine Permission samt erster Bedingung an\n" +
            "> ``edit-permission-role`` ändert Rolle, Modus, Auto-Revoke, Name, Beschreibung\n" +
            "> ``remove-permission-role`` löscht eine Permission, optional mit ``strip-role``\n\n" +
            "**Bedingungen**\n" +
            "> ``add-condition`` hängt eine weitere Bedingung an\n" +
            "> ``remove-condition`` entfernt eine Bedingung über ihre ID\n\n" +
            "**Mitglieder**\n" +
            "> ``grant`` erteilt manuell, optional befristet\n" +
            "> ``revoke`` entzieht manuell, optional befristet\n" +
            "> ``reset`` zurück auf Automatik, optional mit ``rearm-trigger``\n" +
            "> ``info`` zeigt den Stand eines Mitglieds\n\n" +
            "**Überblick**\n" +
            "> ``list`` zeigt alle Permissions mit ihren Bedingungen\n" +
            "> ``settings`` schaltet das globale Auto-Revoke\n" +
            "> ``guide`` diese Anleitung\n\n" +
            "Bei ``permission`` und ``condition`` hilft dir überall die Autovervollständigung.")
    ];

    [ApplicationCommandRequireModerationTeam]
    [SlashCommand("guide", "Erklärt das Extra Permission System mit Beispielen")]
    public static async Task Guide(InteractionContext ctx)
    {
        var pages = GuideChapters.Select((chapter, index) => new Page
        {
            Embed = new DiscordEmbedBuilder
            {
                Title = chapter.Title,
                Description = chapter.Body,
                Color = BotConfig.GetEmbedColor(),
                Footer = new DiscordEmbedBuilder.EmbedFooter
                {
                    Text = $"Seite {index + 1}/{GuideChapters.Length} · /permissionmanagement guide"
                }
            }
        }).ToList();

        await ctx.Interaction.SendPaginatedResponseAsync(false, false, ctx.User, pages,
            behaviour: PaginationBehaviour.Ignore, deletion: ButtonPaginationBehavior.Disable);
    }
}
