#region

using AGC_Management.Entities.Selfroles;

#endregion

namespace AGC_Management.Services;

/// <summary>
///     The catalogue the Python bot kept as dictionaries in <c>exts/selfroles.py</c>, so the system
///     arrives configured instead of empty. "Deine Spiele (1)" and "(2)" are one category here: they were
///     only ever split because Discord caps a select at 25 options, and the menu now paginates by itself.
/// </summary>
public static class SelfroleSeedData
{
    /// <summary>The channel the old panel lived in. Nothing is posted until an admin presses the button.</summary>
    public const ulong SeedChannelId = 752701273043763221;

    public static SelfrolePanel BuildPanel()
    {
        return new SelfrolePanel
        {
            Id = SelfrolePanel.PanelId,
            ChannelId = SeedChannelId,
            HeaderTitle = "Self Roles",
            HeaderText = "Hier kannst du dir Rollen auswählen die dir entsprechen",
            BannerMode = SelfrolePanel.ModeGuild,
            AuthorIconMode = SelfrolePanel.ModeGuild,
            Color = "2F84A2",
            AutoRepost = true,
            Enabled = false
        };
    }

    public static List<SelfroleCategory> BuildCategories()
    {
        return
        [
            Category("dm", "DM Status", SelfroleCategory.KindButtons, "DM Status", 1, 1,
                false, false,
            [
                Option("dm-dms-open", 787426738530549780UL, "DMs Open", "Andere Mitglieder dürfen dich privat anschreiben.", "<:anwesend:946825104829067314>"),
                Option("dm-ask-to-dm", 787427021574635560UL, "Ask to DM", "Andere Mitglieder sollen auf dem Server fragen ob du angeschrieben werden willst.", "<:abwesend:946825104782934046>"),
                Option("dm-dms-closed", 787426900917223434UL, "DMs Closed", "Andere Mitglieder dürfen dich nicht privat anschreiben.", "<:offline:946831431798227056>"),
            ]),
            Category("gender", "Geschlecht", SelfroleCategory.KindButtons, "Dein Geschlecht", 0, 1,
                true, false,
            [
                Option("gender-maennlich", 766659637226504213UL, "Männlich", "", "👦"),
                Option("gender-weiblich", 766660794083180574UL, "Weiblich", "", "👧"),
                Option("gender-divers", 766660799367348224UL, "Divers", "", "🙅"),
            ]),
            Category("platform", "Plattformen", SelfroleCategory.KindSelect, "Deine Plattformen", 0, 0,
                true, false,
            [
                Option("platform-computer", 766659634840076288UL, "Computer", "", "💻"),
                Option("platform-handy", 766664112368779285UL, "Handy", "", "📱"),
                Option("platform-switch", 766659634123112518UL, "Switch", "", "<:nintendoswitch:957622009238540288>"),
                Option("platform-xbox", 766659634110398476UL, "Xbox", "", "<:microsoftxbox:957622804570845214>"),
                Option("platform-playstation", 766659632633872454UL, "Playstation", "", "<:sonyplaystation:957623217399410738>"),
            ]),
            Category("games", "Spiele", SelfroleCategory.KindSelect, "Deine Spiele", 0, 0,
                true, true,
            [
                Option("game-animalcrossing", 752879930622083186UL, "AnimalCrossing", "", "", ["AnimalCrossing"]),
                Option("game-ark", 770754877528997889UL, "Ark", "", "", ["Ark"]),
                Option("game-among-us", 753533069159432295UL, "Among Us", "", "", ["Among Us"]),
                Option("game-bloons-td", 796703413504311369UL, "Bloons TD", "", "", ["Bloons TD"]),
                Option("game-world-of-warcraft", 777552359361478686UL, "World of Warcraft", "", "", ["World of Warcraft"]),
                Option("game-fortnite", 797041343666978817UL, "Fortnite", "", "", ["Fortnite"]),
                Option("game-garrys-mod", 818931149182337035UL, "Garrys Mod", "", "", ["Garrys Mod"]),
                Option("game-soulslike-games", 777552361589309481UL, "Soulslike Games", "", "", ["Soulslike Games"]),
                Option("game-genshin-impact", 760527929917833226UL, "Genshin Impact", "", "", ["Genshin Impact"]),
                Option("game-rocketleague", 759034959922593845UL, "RocketLeague", "", "", ["RocketLeague"]),
                Option("game-supersmashbros", 753729195879825489UL, "SuperSmashBros", "", "", ["SuperSmashBros"]),
                Option("game-minecraft", 752831849692266536UL, "Minecraft", "", "", ["Minecraft"]),
                Option("game-rainbow-six-siege", 752845843404685323UL, "Rainbow Six Siege", "", "", ["Rainbow Six Siege"]),
                Option("game-osu", 751134528013074564UL, "Osu", "", "", ["Osu"]),
                Option("game-valorant", 752844216153145365UL, "Valorant", "", "", ["Valorant"]),
                Option("game-deadbydaylight", 752837332914274346UL, "DeadByDaylight", "", "", ["DeadByDaylight"]),
                Option("game-league-of-legends", 752231346423988297UL, "League of Legends", "", "", ["League of Legends"]),
                Option("game-war-thunder", 1142786500535275602UL, "War Thunder", "", "", ["War Thunder"]),
                Option("game-wuthering-waves", 1325800298454978621UL, "Wuthering Waves", "", "", ["Wuthering Waves"]),
                Option("game-naraka-bladepoint", 1325800070632833074UL, "Naraka Bladepoint", "", "", ["Naraka Bladepoint"]),
                Option("game-gta-5", 753254571287248938UL, "GTA 5", "", "", ["GTA 5"]),
                Option("game-monster-hunter", 845613780913881088UL, "Monster Hunter", "", "", ["Monster Hunter"]),
                Option("game-overwatch", 752846716566503444UL, "Overwatch", "", "", ["Overwatch"]),
                Option("game-brawlhalla", 753533065447342161UL, "Brawlhalla", "", "", ["Brawlhalla"]),
                Option("game-fall-guys", 753242968231641149UL, "Fall Guys", "", "", ["Fall Guys"]),
                Option("game-phasmophobia", 773972107489968181UL, "Phasmophobia", "", "", ["Phasmophobia"]),
                Option("game-call-of-duty", 753291753607397409UL, "Call Of Duty", "", "", ["Call Of Duty"]),
                Option("game-vrchat", 753533065971630091UL, "VRChat", "", "", ["VRChat"]),
                Option("game-need-for-speed", 845613980504031252UL, "Need for Speed", "", "", ["Need for Speed"]),
                Option("game-legends-of-runeterra", 753533070598078504UL, "Legends of Runeterra", "", "", ["Legends of Runeterra"]),
                Option("game-pummelparty", 753533068610109460UL, "PummelParty", "", "", ["PummelParty"]),
                Option("game-counterstrike-2", 753306292591788104UL, "CounterStrike 2", "", "", ["CounterStrike 2"]),
                Option("game-pokemon", 753533067397693490UL, "Pokémon", "", "", ["Pokémon"]),
                Option("game-terraria", 899303183438999633UL, "Terraria", "", "", ["Terraria"]),
                Option("game-roblox", 892700154874986496UL, "Roblox", "", "", ["Roblox"]),
                Option("game-apex-legends", 753306034889556090UL, "Apex Legends", "", "", ["Apex Legends"]),
                Option("game-lost-ark", 941987999321845801UL, "Lost Ark", "", "", ["Lost Ark"]),
                Option("game-diablo", 1107267943403896913UL, "Diablo", "", "", ["Diablo"]),
                Option("game-forza", 1114293310660550686UL, "Forza", "", "", ["Forza"]),
            ]),
            Category("pings", "Pings", SelfroleCategory.KindSelect, "Pings", 0, 0,
                true, false,
            [
                Option("ping-agc-twitch-ping", 1289865903932182589UL, "AGC Twitch Ping", "Benachrichtigungen Twitch-Streams auf unserem AGC-Kanal", ""),
                Option("ping-event-pings", 757203761591484427UL, "Event Pings", "Benachrichtigungen zu Server Events", ""),
                Option("ping-server-updates", 858453560659935233UL, "Server Updates", "Benachrichtigungen von Server Updates bekommen", ""),
                Option("ping-fabi-chan-ping", 939215043357192234UL, "Fabi-Chan Ping", "Pings von Fabi-Chan (Twitch/Youtube) und Channel freischalten (Freiwillig)", ""),
                Option("ping-umfrage-ping", 1081295495420457021UL, "Umfrage-Ping", "Pings wenn wir Umfragen veranstalten", ""),
                Option("ping-bump-ping", 904455037344968705UL, "Bump Ping", "Benachrichtigung wenn man den Server bumpen kann", ""),
            ]),
        ];
    }

    private static SelfroleCategory Category(string id, string name, string kind, string placeholder, int minValues,
        int maxValues, bool allowClear, bool autoAssign, List<SelfroleOption> options)
    {
        var category = new SelfroleCategory
        {
            Id = id,
            Name = name,
            Kind = kind,
            Placeholder = placeholder,
            MinValues = minValues,
            MaxValues = maxValues,
            AllowClear = allowClear,
            AutoAssign = autoAssign,
            Options = options
        };

        for (var index = 0; index < options.Count; index++)
        {
            options[index].CategoryId = id;
            options[index].Position = index;
        }

        return category;
    }

    private static SelfroleOption Option(string id, ulong roleId, string label, string description, string emoji,
        string[]? matchPatterns = null)
    {
        return new SelfroleOption
        {
            Id = id,
            RoleId = roleId,
            Label = label,
            Description = description,
            Emoji = emoji,
            MatchPatterns = matchPatterns ?? []
        };
    }
}
