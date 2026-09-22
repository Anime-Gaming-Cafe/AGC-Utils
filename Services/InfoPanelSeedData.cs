#region

using AGC_Management.Entities.InfoPanels;

#endregion

namespace AGC_Management.Services;

/// <summary>
///     The German texts the Python bot carried in <c>data/infotext.py</c>, copied over unchanged so the
///     panel works on the first start. They are seed values only: once a row exists the dashboard owns it
///     and <see cref="DatabaseService" /> never writes over it again.
/// </summary>
public static class InfoPanelSeedData
{
    public const string PanelId = "regelwerk";

    public const string GroupButtons = "buttons";
    public const string GroupRules = "rules";
    public const string GroupInfos = "infos";

    /// <summary>
    ///     06.01.2025 16:00, the value the old bot had hardcoded as <c>regelzeit</c>. Seeding the real date
    ///     keeps the panel from claiming the rules changed on the day of the deploy.
    /// </summary>
    public static readonly long SeedUpdatedAt =
        new DateTimeOffset(2025, 1, 6, 16, 0, 0, TimeSpan.FromHours(1)).ToUnixTimeSeconds();

    public const string HeaderTitle = @"Herzlich Willkommen auf dem Anime & Gaming Café";
    public const string AuthorName = @"Anime & Gaming Café";
    public const string SpacerUrl = @"https://i.imgur.com/U9Fih4D.png";

    public const string BirthdayImageUrl =
        @"https://cdn.discordapp.com/attachments/764921088689438771/950057781203959848/biSQObD.gif";

    public const string HeaderText =
        @"Finde neue Freunde in der größten deutschsprachigen Anime und Gaming Community auf Discord. Entdecke neue und dir bisher unbekannte Animes, Mangas und Spiele! Nimm an unzähligen Events,  Animenights, Giveaways und vielem mehr teil! Wir freuen uns darauf  dich hier bei uns willkommen zu heißen  

**Invite-Link**
https://discord.gg/animegamingcafe";

    public const string TeamText =
        @"Du kannst dich jederzeit mit deinem serverbetreffenden Anliegen bei unserem Team melden. Unser Ziel ist es, stets eine freundliche und trollfreie Community zu bewahren.

** In überarbeitung**

";

    public const string MainRules =
        @"
``1.``	__Spam:__ Was Spam bedeutet sollte für jeden klar sein (das wiederholte Schicken einer Nachricht / eines Emotes in ausgesprochen kurzen Abständen). Um ein angenehmes Chatklima zu wahren, ist JEGLICHE Art von Spam untersagt.

``2.``	__Umgangston:__ Der Umgang mit anderen Usern sollte stets freundlich sein. Verbale Angriffe jeglicher Art gegenüber anderen Usern sind untersagt und werden bestraft. Genauso sind diverse Provokationen (bspw. Herziehen, Blockierung unter die Nase reiben, etc.) zu unterlassen. Fühlt sich eine Person unwohl und gibt uns bescheid, kann dies mit einem Warn bestraft werden.

``3.``	__Streaming/Aufnahmen:__ Das Mitschneiden von Clips innerhalb der Voicechats ist untersagt. (Ausnahme: Supportfälle) 

``4.``	__NSFW:__ Das Verbreiten von NSFW (pornographische/gewaltdarstellende/rassistischen u.ä.) Inhalten ist verboten **Siehe [Discord Partner ToS](https://support.discord.com/hc/de/articles/360024871991-Verhaltenskodex-f%C3%BCr-Discord-Partnerschaften)**.

``5.``	__Channelhopping:__ Channelhopping (das wiederholte Springen innerhalb kurzer Zeit von einem in den anderen Channel) ist nicht gestattet.

``6.``  __Bitte beachte die Chatbeschreibungen.__

``7.``	__Werbung:__ Jegliche Art von Werbung ist auf diesem Server untersagt. Ebenso ist das Abwerben von Leuten zu anderen Servern untersagt und wird mit einem Bann bestraft. DM - Invite = Bann!

``8.``	__Datenschutz:__ Private Daten, wie Telefonnummern, Adressen, Passwörter, IPs und Ähnliches dürfen nicht öffentlich ausgetauscht werden.

``9.``	__Soundboards:__ Das Nutzen von Soundboards, sowie die Nutzung von Stimmenverzerrern ist nicht gestattet.(Ausnahme: In Custom VCs ist es, falls es mit den Usern dieses Channels abgesprochen ist, erlaubt) Zudem ist Earrape in jeder Form untersagt und wird im schlimmsten Fall mit einem Bann bestraft.

``10.``	__Ticket/Support:__ Report’s sind über den [Ticketbot](https://discord.com/channels/750365461945778209/826083443489636372/930477800496979969) oder direkt über das Serverteam zu machen. Dort kannst du ebenfalls Fragen zu Verwarnungen und weiteren Serverrelevanten Themen stellen.

``11.``	__Imitation:__ Das Imitieren bzw. Nachahmen von Mitgliedern des Serverteams ist untersagt.

``12.`` __Catfishing:__ Es ist verboten sich als jemand anderes auszugeben und damit Falschinformationen über sich zu verbreiten. Ebenfalls Account-Sharing

``13.`` __Schädliche Dateien:__ Das Verbreiten von schädlichen Links, Bildern, Texten, Dateien oder Ähnlichem ist in jeglicher Form verboten.

``14.`` __Profil:__ Kontonamen, Bilder und Status dürfen weder beleidigend, rassistisch, sexistisch, diskriminierend oder moralisch inkorrekt sein.

``15.`` __Das Leaken von DMs ist in keinem Falle erlaubt!__

``16.`` __Beihilfe/Unterstützung zum Regelbruch wird bestraft!__

``17.`` __Spoiler:__ Das Spoilern von jeglicher Art von Informationen ist untersagt.

``18.`` __Aufruhr vermeiden:__ Solltest du eine Verwarnung erhalten haben, teile dies und die gesamte Situation nicht auf auf dem Server. Dies dient der Vorbeugung von Auseinandersetzung im Chat. Bei Anliegen zur Verwarnung -> [Ticketbot](https://discord.com/channels/750365461945778209/826083443489636372/930477800496979969) -> Um ein Aufruhr zu vermeiden, sollte nicht über gesperrte oder gemutete Mitglieder gesprochen werden.

``19.`` __Es gelten außerdem die __[Terms of Service](https://discord.com/terms)__ und die __[Discord Partner ToS](https://support.discord.com/hc/de/articles/360024871991-Verhaltenskodex-f%C3%BCr-Discord-Partnerschaften)__ von Discord.__

``20.`` __Serverteam respektieren:__ Die Entscheidungen des Serverteams sind zu respektieren.

__Mit dem Beitritt des Servers erklärst du dich mit den Regeln einverstanden!__
";

    public const string ChatRules =
        @"
__Chatregeln:__

__- Emotes:__

Emotes sind schön und gut, es muss aber nicht übertrieben werden. Sie sind zwar unverzichtbar aber nicht ausschlaggebend für ein Gespräch, darum benutzt leerstehende Emotes bitte nur im Kontext oder als Reaktion auf Nachrichten.
Teammitglieder werden hier nach eigenem Ermessen handeln, sobald diese es als kontextlos erachten oder diese als Spam deklarieren.

__- AutoMod:__

Das umgehen des Automoderators mit umschreiben oder Screenshotten um Beleidigungen o.ä. zu nutzen, ist nicht erlaubt und kann mit einer Verwarnung o.ä bestraft werden.

__- Caps:__

Keiner mag es angeschrien zu werden, also versucht daher Caps zu vermeiden.

Ghostpings sind ebenfalls zu unterlassen.

__- Politische/Religiöse Themen:__

Das Diskutieren dieser Themen (Beispiel: Ukraine-Russland Krieg) ist nicht erwünscht.


__- Freundlichkeit und Respekt gegenüber anderen:__

Wir sind ein freundlicher Server, daher ist es wichtig, dass toxisches, provokatives und beleidigendes Verhalten vermieden wird. Sollte es doch einmal vorkommen, dann meldet uns dies bitte und erwidert nicht dieses Verhalten.
Genauso sind die Provokationen gegen andere Mitglieder die beispielsweiße neu sind, nicht erwünscht. Wir wollen hier eine freundliche Community aufbauen und nicht eine, die sich gegenseitig provoziert. 
Beispiele: (""Und du bist wer?"", ""Wer hat gefragt"") etc. Unangenehmes und unangebrachtes Verhalten ist ebenfalls nicht erwünscht.
Missachtung dieser Regel kann zu einer Verwarnung und im allerschlimmsten Fall zu einem Bann führen.

__- Common Sense:__

Bitte vermeidet es über unangemessene oder unangenehme Themen zu schreiben. Bei Bedarf steht es dem Serverteam frei einzuschreiten. Benutzt euren gesunden Menschenverstand. **Das Thematisieren von Drogen ist hier nicht erlaubt!**

__- Sprache:__

Wir sind ein deutschsprachiger Server, deshalb ist unser Hauptchat Just Chatting bitte hauptsächlich in Deutsch zu halten. Ausgenommen ist <#771174633230827540> da dies unser englischsprachiger Chat ist.
In <#750365462524854331> ist ebenfalls das Schreiben in anderen Sprachen nicht erwünscht. 

";

    public const string VoiceRules =
        @"

``1.`` Der Umgang mit andern Usern ist stets freundlich zu führen. (Rede so mit den Usern wie auch mit dir gesprochen werden soll) Bei Verstößen zu melden -> [Ticketbot](https://discord.com/channels/750365461945778209/826083443489636372/930477800496979969)

``2.`` Das Aufnehmen von Gesprächen ist verboten. **Siehe [ToS](https://discord.com/terms)**

``3.`` User in die Irre zu führen ist verboten und wird bestraft.

``4.`` Das Team darf jederzeit unangemeldet in den Voice Chat joinen um eine Situation zu kontrollieren/beruhigen.

``5.`` Das konsumieren/zeigen/verheerlichen von Drogen ist verboten.

``6.`` AFK-Farming in den Voice-Chats ist verboten. Ausnahme hierbei sind Sleepcalls, die ab und zu stattfinden. Wenn festgestellt wird, dass ein Voice-Chat dazu genutzt wird um Level und/oder Vorteile von ""Spielbots"" zu erfarmen, dürfen diese jederzeit vom Moderationsteam geschlossen werden und bei wiederholenden Taten auch bestraft werden.

";

    public const string DmRules =
        @"(DM = Direct Message = Privatnachrichten, Privatchats)

Die DM-Rollen müssen ernst genommen werden. Sollten diese missachtet werden, kann dies verwarnt werden, sofern dies uns gemeldet wird. (Teammitglieder sind in Support-Situationen davon ausgeschlossen und haben ebenso grundsätzlich offene DM's bei Dingen, die mit dem Server zu tun haben)

__**Was bedeutet:**__

__Open DM's:__ Du darfst diese Person anschreiben
__Ask to DM's:__ Frag auf dem Server zuerst nach, ob die Person von dir angeschrieben werden möchte
__Closed DM's:__ Du darfst diese Person nicht anschreiben.";

    public const string TempVcRules =
        @"

``1.`` Der Besitzer hat das alleinige Entscheidungsrecht wer *SEINEM* Channel beitreten darf. 

``2.`` Solltet ihr einen User aus eurem Channel blocken wollen, weil ihr ihn grundsätzlich nicht darin haben wollt, so blockiert ihn bitte einmalig und speichert den Channel mit ``-session save``, anstatt ihn immer wieder zu pingen oder blockt ihn mit der User ID.

``3.`` **Das Moven** aus eurem Channel in andere Channel ist untersagt und zu unterlassen !! Bei Missachtung ist mit einer Verwarnung zu rechnen. 

``4.`` Vor Benutzung eines Soundboards ist mit den Usern des Temp Channels abzusprechen, ob diese in ihrem Channel geduldet werden (Earrape jeglicher Art ist dennoch verboten und kann mit einem Ausschluss aus dem Server bestraft werden).

``5.`` Es dürfen keine anstößigen Wörter als Channelnamen verwendet werden und ein Verstoß **kann** mit einem Warn bestraft werden. -> ToS angemessene Channelnamen
";

    public const string SecondAccounts =
        @"Es ist __grundsätzlich gestattet__ einen 2. Account auf den Server zu holen. __Allerdings müssen folgende Voraussetzungen erfüllet werden__:


``1.`` **__Der Account ist beim Serverteam im Ticketbot anzumelden.__** (Unter Angabe eines Grundes) -> [Ticketbot](https://discord.com/channels/750365461945778209/826083443489636372/930477800496979969)

``2.`` Zweitaccounts müssen mit dem Mainaccount identifizierbar oder in Verbindung gebracht werden können.

``3.`` Nebenaccounts dürfen nicht zu Abhör- bzw. ""Spionagezwecken"" missbraucht werden.

``4.`` Banumgehung mit einem 2. Account führt zum direkten Ausschluss des AGC-Servers.

``5.`` 2. Accounts dürfen nur von der Person genutzt werden von der sie angemeldet wurden. Die Weitergabe eines 2. Accounts wird für beide Beteiligten, je nach Situation eine Strafe mit sich ziehen.
";

    public const string LegalReservation =
        @"
``1.`` __Allgemeiner Rechtsvorbehalt:__ Teammitglieder behalten sich das Recht vor, außerhalb der angegebenen Verbote zu handeln und zu sanktionieren. (Banns dürfen direkt verteilt werden, ohne vorher zu warnen) Mit dem Beitritt auf den AGC-Server akzeptieren alle User die [Discord ToS](https://discord.com/terms), [Discord Partner ToS](https://support.discord.com/hc/de/articles/360024871991-Verhaltenskodex-f%C3%BCr-Discord-Partnerschaften) sowie das gesamte Regelwerk des Anime & Gaming Café Discord Servers. Auskunft über Banns wird nicht an dritte weitergegeben - dies gilt auch für Gründe der Sanktionierungen. Änderungen im Regelwerk sind uns vorbehalten und können jederzeit auftreten. Daher muss sich jeder User regelmäßig in angemessenem Umfang über unser Regelwerk informieren.

``2.``	__Punishmentsystem:__ 
```
- 1 Warn = Warnung in den DMs vom Bot 
- 2 Warn = Kick vom Server
- 3 Warn = Ban mit möglichem Entbannungsantrag (Nur 1x möglich)

 Verwarnungen verjähren.```                                  

``3.``	__Weisungsrecht:__ Administratoren, Moderatoren und Supporter haben volles Weisungsrecht. Das Verweigern einer bestimmten Anweisung kann zu einem Kick, Bann oder Ähnlichem führen. Falls du das Bedürfnis verspürst jemanden aus dem Team zu blockieren, steht dir das frei, jedoch bist du selbst dafür verantwortlich, falls du dadurch Anweißungen dieser, nicht mitbekommst.

``4.``	__Kicken/Bannen:__ Ein Kick oder Ban ist zu keinem Zeitpunkt unbegründet, sondern soll zum Nachdenken der eigenen Verhaltensweise anregen. Entbannungen müssen dem Serverteam über einen Entbannungsantrag im [Entbannungsportal](https://unban.animegamingcafe.de) gestellt werden. Bannumgehung in jeglicher Art und Weise ist untersagt und wird eine Entbannung signifikant erschweren.

``5.``	__Links:__ Wir übernehmen keine Verantwortung für Links, welche nicht von uns gepostet wurden.

``6.`` __Aufruhr vermeiden:__ Solltest du eine Verwarnung erhalten haben, teile dies und die gesamte Situation nicht auf auf dem Server. Dies dient der Vorbeugung von Auseinandersetzung im Chat. Bei Anliegen zur Verwarnung -> [Ticketbot](https://discord.com/channels/750365461945778209/826083443489636372/930477800496979969)

``7.``	__Rechtfertigung:__ Mitglieder des Serverteams müssen sich nicht rechtfertigen, solange sie nicht von einem ranghöheren Mitglied dazu aufgefordert werden.

``8.`` __Betteln um Rollen:__ Das betteln um Rollen und Ränge ist untersagt!


```Serverinhalte (z.B. Regelwerk, Benennenung der Channel, Rollensystem usw.) zu kopieren und diese auf fremden Discord Servern zu nutzen ist untersagt. Dies kann mit einer Verwarnung bis zu einem Bann geahndet werden.```
";

    public const string LevelSystem =
        @"__Unsere Ränge:__

<@&750450099871547462> = Level 0 (Beim Beitritt / Bei der Verifikation)
<@&750402390691152005> = Level 5 ``(Schaltet Medienperms im Main-Chat frei)``
<@&798562254408777739> = Level 10
<@&750450170189185024> = Level 15
<@&798555933089071154> = Level 20
<@&750450342474416249> = Level 25
<@&750450621492101280> = Level 30
<@&798555135071617024> = Level 35
<@&751134108893184072> = Level 40
<@&776055585912389673> = Level 45
<@&750458479793274950> = Level 50
<@&798554730988306483> = Level 60
<@&757683142894157904> = Level 70
<@&810231454985486377> = Level 80
<@&810232899713630228> = Level 90
<@&810232892386705418> = Level 100 
<@&750500017118249141> = Server Booster


__**Wie bekommt man XP?**__

Mit dem Chatten hier bekommt man __pro__ Nachricht __minütlich__ zwischen ``15-25 XP``
Zusätlich kannst du auch im Voice Chat zwischen ``2-5 XP`` pro Minute erhalten

__Um Spam zu vermeiden, bekommst du nur ``1x`` in der Minute XP__


__**Was bringen mir die Level?**__

Du bekommst eine höhere Listung an der rechten Seite und eine andere Farbe";

    public const string WebPresence =
        @"
Wir sind auch auf __Social Media__ und im __Web__ vertreten!

Besuche doch unsere [Website](https://animegamingcafe.de).

Besuche doch unser [Instagram](https://instagram.com/animegamingcafe).
";

    public const string SocialGroups =
        @"
Twitch Kanal:
<https://twitch.tv/animegamingcafe>

Instagram Account:
<https://instagram.com/animegamingcafe>

Steam Gruppe:
<https://steamcommunity.com/groups/animegamingcafe>

VRChat Gruppe:
<https://vrchat.com/home/group/grp_89d49027-3a69-43df-8824-441f2b43997c>

MyAnimeList Gruppe (Club):
<https://myanimelist.net/clubs.php?cid=89115>

";

    public const string SupportUs =
        @"
Es gibt verschiedene Methoden wie du uns unterstützen kannst!

``1.`` ~~Wenn ihr den Server am Leben erhalten wollt und ihn unterstützen wollt, voted doch auf [Top.gg](https://top.gg/servers/750365461945778209/vote). Dies ist für die Serverliste. (voten ist alle 12h möglich) Du bekommst dann die Rolle <@&759802968035426304>~~ TopGG hat die Funktion eingestellt.

``2.`` Die zweite Option ist nur einmalig. 
Hier kannst du auf [Disboard](https://disboard.org/de/server/750365461945778209) eine Rezension/Bewertung über den Server da lassen. 
Wir freuen uns über jede konstruktive Bewertung!

``3.`` Die dritte Option ist kostenpflichtig (Einmalzahlung - Keine Benefits).
Ihr könnt uns unter [Ko-Fi](https://ko-fi.com/animegamingcafe) einen kleinen Beitrag leisten. Wir freuen uns über jeden Beitrag! ❤

``4.`` Die vierte Option ist ebenfalls kostenpflichtig (wiederkehrende Subskription (Kündigbar - Serverbenefits (Xp-boost etc)).
Ihr könnt uns unter [Patreon](https://patreon.com/animegamingcafe) unterstützen. ❤";

    public const string Badges =
        @"
Wofür habe ich <@&875679019054551050>?

Du kannst auf diesem Server Medaillen verdienen, indem du bei uns an speziellen Events teilnimmst oder aktiv im Chat/Voicechat bist. Diese Medaillen erhältst du in Form von einer Rolle.";

    public const string Unban =
        @"
Falls du denkst du wurdest zu unrecht gebannt, kannst du hier einen Entbannungsantrag stellen: [AGC Entbannportal](https://unban.animegamingcafe.de/)

**(Entbannungen werden nicht anders bearbeitet, auch nicht in den DMs der Administration/Serverleitung)**
";

    public const string Birthdays =
        @"
Wir haben einen eigenen Bot mit dem es möglich ist, dass du deinen Geburtstag eintragen kannst.

__**Was bringt mir das?**__

Du bekommst an deinem Geburtstag die Rolle <@&919973830485749770> und stehst somit **über** den Boostern also **direkt unter** dem Team! Du bekommst, falls nicht deaktiviert, einen Ping in <#920063104770015312> damit dir die Mitglieder gratulieren können.
Das Setup führst du aus mit ``agc!bday``";

    public const string PrivateVoice =
        @"
Du kannst dir in <#930875444759261204> einen eigenen Voice Channel erstellen.

Um einen Channel erstellen zu können, benötigst du eine der beiden Voraussetzungen:

__**Wie kann ich meinen Channel einstellen?**__
Siehe <#930878036377747456>
";

    /// <summary>The panel exactly as the Python bot rendered it, minus the disabled statistics button.</summary>
    public static InfoPanel BuildSeedPanel()
    {
        var panel = new InfoPanel
        {
            Id = PanelId,
            Name = "Regelwerk",
            ChannelId = 0,
            MessageId = 0,
            Enabled = false,
            HeaderTitle = HeaderTitle,
            HeaderText = HeaderText,
            AuthorName = AuthorName,
            AuthorIconMode = InfoPanel.ModeGuild,
            BannerMode = InfoPanel.ModeGuild,
            SpacerUrl = SpacerUrl,
            Color = "2F3136",
            AutoRepost = true
        };

        var buttons = new InfoPanelGroup
        {
            Id = GroupButtons,
            PanelId = PanelId,
            Kind = InfoPanelGroup.KindButtons,
            Position = 0
        };
        buttons.Pages.AddRange(
        [
            Link("selfroles", GroupButtons, "Selfroles",
                "https://discord.com/channels/750365461945778209/752701273043763221/", 0),
            Link("entbannungsserver", GroupButtons, "Entbannungsserver", "https://discord.gg/mmky4WUG2j", 1),
            Text("team", GroupButtons, "Das Server Team", "", "Das Anime & Gaming Café Team", TeamText, 2,
                "103674")
        ]);

        var rules = new InfoPanelGroup
        {
            Id = GroupRules,
            PanelId = PanelId,
            Kind = InfoPanelGroup.KindSelect,
            Placeholder = "🧾 Regelübersicht",
            DateLabel = "Stand der Regeln",
            Position = 1
        };
        rules.Pages.AddRange(
        [
            Text("rule1", GroupRules, "1. Allgemeine Regeln", "", "Allgemeine Serverregeln", MainRules, 0),
            Text("rule2", GroupRules, "2. Allgemeine Chatregeln", "", "Allgemeine Chatregeln", ChatRules, 1),
            Text("rule3", GroupRules, "3. Allgemeine Voicechat Regeln", "", "Allgemeine Voicechat Regeln",
                VoiceRules, 2),
            Text("rule4", GroupRules, "4. DM Regeln", "", "DM Regeln", DmRules, 3),
            Text("rule5", GroupRules, "5. Custom VC Regeln", "", "Custom VC Regeln", TempVcRules, 4),
            Text("rule6", GroupRules, "6. 2. Accounts", "", "2. Accounts auf AGC", SecondAccounts, 5),
            Text("rule7", GroupRules, "7. Rechtsvorbehalt", "", "Rechtsvorbehalt", LegalReservation, 6)
        ]);

        var infos = new InfoPanelGroup
        {
            Id = GroupInfos,
            PanelId = PanelId,
            Kind = InfoPanelGroup.KindSelect,
            Placeholder = "📢 Erweiterte Informationen",
            DateLabel = "Stand der Infos",
            Position = 2
        };
        infos.Pages.AddRange(
        [
            Text("info1", GroupInfos, "1. Levelsystem", "", "Das Levelsystem", LevelSystem, 0),
            Text("info2", GroupInfos, "2. Internetauftritt", "", "Der Internetauftritt", WebPresence, 1),
            Text("grpinfo", GroupInfos, "2.1 Server Gruppen und Social Media Konten", "",
                "Server Gruppen und Social Media Konten", SocialGroups, 2),
            Text("info3", GroupInfos, "3. Unterstütze Uns!", "", "Unterstütze uns!", SupportUs, 3),
            Text("info4", GroupInfos, "4. Medallien und Auszeichnungen", "", "Medallien und Auszeichnungen",
                Badges, 4),
            Text("info5", GroupInfos, "5. Entbannung", "", "Entbannungen", Unban, 5),
            Text("info6", GroupInfos, "6. Geburtstage", "", "Geburtstage", Birthdays, 6,
                imageUrl: BirthdayImageUrl),
            Text("info7", GroupInfos, "7. Private Voice Channel", "", "Private Voice Channel", PrivateVoice, 7)
        ]);

        panel.Groups.AddRange([buttons, rules, infos]);
        return panel;
    }

    private static InfoPanelPage Text(string id, string groupId, string label, string description, string title,
        string content, int position, string color = "", string imageUrl = "")
    {
        return new InfoPanelPage
        {
            Id = id,
            GroupId = groupId,
            PanelId = PanelId,
            Kind = InfoPanelPage.KindText,
            Label = label,
            Description = description,
            Title = title,
            Content = content,
            Color = color,
            ImageUrl = imageUrl,
            Position = position,
            UpdatedAt = SeedUpdatedAt
        };
    }

    private static InfoPanelPage Link(string id, string groupId, string label, string url, int position)
    {
        return new InfoPanelPage
        {
            Id = id,
            GroupId = groupId,
            PanelId = PanelId,
            Kind = InfoPanelPage.KindLink,
            Label = label,
            Url = url,
            Position = position,
            UpdatedAt = SeedUpdatedAt
        };
    }
}
