#region

using System.Text;
using AGC_Management.Services;
using DisCatSharp.Interactivity.Extensions;

#endregion

namespace AGC_Management.Utils;

public static class ModerationHelper
{
    private static readonly int FallbackWarnsToKick = 2;
    private static readonly int FallbackWarnsToBan = 3;
    private static readonly bool FallbackWarnsToBanEnabled = false;
    private static readonly bool FallbackWarnsToKickEnabled = false;


    public static async Task<(bool, bool)> UserActioningEnabled()
    {
        bool WarnsToKickEnabled;
        bool WarnsToBanEnabled;
        try
        {
            WarnsToBanEnabled = bool.Parse(BotConfig.GetConfig()["ModerationConfig"]["WarnsToBanEnabled"]);
        }
        catch
        {
            WarnsToBanEnabled = FallbackWarnsToBanEnabled;
        }

        try
        {
            WarnsToKickEnabled = bool.Parse(BotConfig.GetConfig()["ModerationConfig"]["WarnsToKickEnabled"]);
        }
        catch
        {
            WarnsToKickEnabled = FallbackWarnsToKickEnabled;
        }

        return (WarnsToKickEnabled, WarnsToBanEnabled);
    }

    public static async Task KickFlag(DiscordUser user, string reason, DiscordUser mod)
    {
        var prefix = "KICK-";
        var caseid = prefix + ToolSet.GenerateCaseID();
        Dictionary<string, object> data = new()
        {
            { "userid", (long)user.Id },
            { "punisherid", (long)mod.Id },
            { "datum", DateTimeOffset.Now.ToUnixTimeSeconds() },
            { "description", reason },
            { "caseid", caseid }
        };
        await DatabaseService.InsertDataIntoTable("flags", data);
    }

    /// <summary>
    ///     Records a manual Extra-Permission override as a normal flag, same shape as
    ///     <see cref="KickFlag" />. Returns the case id so the caller can name it in its response.
    /// </summary>
    public static async Task<string> PermissionFlag(DiscordUser user, DiscordUser mod, string description)
    {
        var caseid = "PERM-" + ToolSet.GenerateCaseID();
        Dictionary<string, object> data = new()
        {
            { "userid", (long)user.Id },
            { "punisherid", (long)mod.Id },
            { "datum", DateTimeOffset.Now.ToUnixTimeSeconds() },
            { "description", description },
            { "caseid", caseid }
        };
        await DatabaseService.InsertDataIntoTable("flags", data);
        return caseid;
    }

    public static async Task<(int, int)> GetWarnKickValues()
    {
        int WarnsToKick;
        int WarnsToBan;
        try
        {
            WarnsToKick = int.Parse(BotConfig.GetConfig()["ModerationConfig"]["WarnsToKick"]);
        }
        catch
        {
            WarnsToKick = FallbackWarnsToKick;
        }

        try
        {
            WarnsToBan = int.Parse(BotConfig.GetConfig()["ModerationConfig"]["WarnsToBan"]);
        }
        catch
        {
            WarnsToBan = FallbackWarnsToBan;
        }

        if (WarnsToKick <= 0) throw new Exception("WarnsToKick must be greater than 0!");

        if (WarnsToKick >= WarnsToBan) throw new Exception("WarnsToKick must be lower than WarnsToBan!");

        return (WarnsToKick, WarnsToBan);
    }


    public static async Task<DiscordEmbed> GenerateWarnEmbed(CommandContext ctx, DiscordUser user, DiscordUser mod,
        int warnCount, string caseid, bool isManual, string reason, bool isPerma = false)
    {
        var unbanurl = GetUnbanURL();
        var (warnsToKick, warnsToBan) = await GetWarnKickValues();
        var introPhrase = isPerma
            ? "Du hast eine permanente Verwarnung (sie läuft nicht ab) vom Serverteam erhalten"
            : "Du hast eine Verwarnung vom Serverteam erhalten";
        var title = isPerma ? "Du wurdest permanent verwarnt!" : "Du wurdest verwarnt!";

        if (warnCount >= warnsToBan)
            return new DiscordEmbedBuilder()
                .WithDescription(
                    $"{introPhrase}, bitte beachte, dass Verwarnungen immer Folgen mit sich ziehen. Dies ist deine **{warnCount}. Verwarnung**. __Du wirst nun aus dem Server gebannt.__ Du kannst einen [Entbannungsantrag einreichen]({unbanurl}). Der Grund für die Verwarnung ist: ```{reason}```")
                .WithColor(DiscordColor.Red).WithFooter("").WithFooter(isManual
                    ? $"Manuelle Verwarnung | Warnungs-ID: {caseid} <- Bei Fragen bitte ein Ticket eröffnen und diese ID angeben."
                    : $"Automatische Verwarnung | Warnungs-ID: {caseid} <- Bei Fragen bitte ein Ticket eröffnen und diese ID angeben.")
                .WithTitle(title)
                .Build();

        if (warnCount == warnsToBan - 1)
            return new DiscordEmbedBuilder()
                .WithDescription(
                    $"{introPhrase}, bitte beachte, dass Verwarnungen immer Folgen mit sich ziehen. Dies ist deine **{warnCount}. Verwarnung**, " +
                    $"__beachte bitte, dass Du bei der nächsten Verwarnung aus **{ctx.Guild.Name}** gebannt wirst.__ Der Grund für die Verwarnung ist: ```{reason}```")
                .WithColor(DiscordColor.Red)
                .WithTitle(title)
                .Build();

        if (warnCount >= warnsToKick)
        {
            if (warnsToKick + 1 == warnsToBan)
                return new DiscordEmbedBuilder()
                    .WithDescription(
                        $"{introPhrase}, bitte beachte, dass Verwarnungen immer Folgen mit sich ziehen. Dies ist deine **{warnCount}. Verwarnung**, " +
                        $"__beachte bitte, dass Du bei der nächsten Verwarnung aus **{ctx.Guild.Name}** gebannt wirst.__ {(isPerma ? "Du wirst nun aus dem Server gekickt." : "__Du wirst nun aus dem Server gekickt.__")} Der Grund für die Verwarnung ist: ```{reason}```")
                    .WithColor(DiscordColor.Red).WithFooter(isManual
                        ? $"Manuelle Verwarnung | Warnungs-ID: {caseid} <- Bei Fragen bitte ein Ticket eröffnen und diese ID angeben."
                        : $"Automatische Verwarnung | Warnungs-ID: {caseid} <- Bei Fragen bitte ein Ticket eröffnen und diese ID angeben.")
                    .WithTitle(title)
                    .Build();
            return new DiscordEmbedBuilder()
                .WithDescription(
                    $"{introPhrase}, bitte beachte, dass Verwarnungen immer Folgen mit sich ziehen. Dies ist deine {(isPerma ? $"**{warnCount}. Verwarnung**," : $"**{warnCount}.")} __Du wirst nun aus dem Server gekickt.__ Der Grund für die Verwarnung ist: ```{reason}```")
                .WithColor(DiscordColor.Red).WithFooter(isManual
                    ? $"Manuelle Verwarnung | Warnungs-ID: {caseid} <- Bei Fragen bitte ein Ticket eröffnen und diese ID angeben."
                    : $"Automatische Verwarnung | Warnungs-ID: {caseid} <- Bei Fragen bitte ein Ticket eröffnen und diese ID angeben.")
                .WithTitle(title)
                .Build();
        }

        if (warnCount == warnsToKick - 1)
            return new DiscordEmbedBuilder()
                .WithDescription(
                    $"{introPhrase}, bitte beachte, dass Verwarnungen immer Folgen mit sich ziehen. Dies ist deine **{warnCount}. Verwarnung**, __beachte bitte, dass Du bei der nächsten Verwarnung aus **{ctx.Guild.Name}** gekickt wirst.__ Der Grund für die Verwarnung ist: ```{reason}```*")
                .WithColor(DiscordColor.Red).WithFooter(isManual
                    ? $"Manuelle Verwarnung | Warnungs-ID: {caseid} <- Bei Fragen bitte ein Ticket eröffnen und diese ID angeben."
                    : $"Automatische Verwarnung | Warnungs-ID: {caseid} <- Bei Fragen bitte ein Ticket eröffnen und diese ID angeben.")
                .WithTitle(title)
                .Build();

        return new DiscordEmbedBuilder()
            .WithDescription(
                $"{introPhrase}, bitte beachte, dass Verwarnungen immer Folgen mit sich ziehen. Dies ist deine **{warnCount}. Verwarnung**. Der Grund für die Verwarnung ist: ```{reason}```*")
            .WithColor(DiscordColor.Red).WithFooter(isManual
                ? $"Manuelle Verwarnung | Warnungs-ID: {caseid} <- Bei Fragen bitte ein Ticket eröffnen und diese ID angeben."
                : $"Automatische Verwarnung | Warnungs-ID: {caseid} <- Bei Fragen bitte ein Ticket eröffnen und diese ID angeben.")
            .WithTitle(title)
            .Build();
    }

    public static async Task<string?> ReasonSelector(CommandContext ctx, string reasonTable, string actionTitle)
    {
        var caseId = ToolSet.GenerateCaseID();
        List<string> DBQuery =
		[
			"*"
        ];
        var queryResult =
            await DatabaseService.SelectDataFromTable(reasonTable, DBQuery, null);
        var embedBuilder = new DiscordEmbedBuilder()
            .WithTitle($"Grund auswählen | Aktion: {actionTitle}")
            .WithDescription(
                "Wähle einen oder mehrere zutreffende Gründe aus der Liste aus. \n Dieses Menü läuft in 120 Sekunden ab!")
            .WithFooter(ctx.User.GetFormattedUserName())
            .WithColor(BotConfig.GetEmbedColor());
        var options = new List<DiscordStringSelectComponentOption>();
        Dictionary<string, string> ReasonMap = [];
        foreach (var result in queryResult)
        {
            options.Add(new DiscordStringSelectComponentOption(result["reason"].ToString(),
                result["custom_id"].ToString()));
            ReasonMap.Add(result["custom_id"].ToString(), result["reason"].ToString());
        }

        var discordEmbed = embedBuilder.Build();
        var select = new DiscordStringSelectComponent("Gründe auswählen", options, caseId,
            maxOptions: ReasonMap.Count, minOptions: 1);
        var selector = new List<DiscordComponent>
        {
            select
        };

        List<DiscordActionRowComponent> discordActionRowComponents =
		[
			new DiscordActionRowComponent(selector)
        ];
        var message = await ctx.RespondAsync(new DiscordMessageBuilder().AddEmbed(discordEmbed)
            .AddComponents(discordActionRowComponents));
        var interactivity = ctx.Client.GetInteractivity();
        var interaction = await interactivity.WaitForSelectAsync(message, ctx.User, caseId,
            ComponentType.StringSelect, TimeSpan.FromSeconds(120));
        if (interaction.TimedOut)
        {
            await message.DeleteAsync();
            return "";
        }


        var valIds = new List<string>();
        foreach (var option in interaction.Result.Values) valIds.Add(option);

        var reasonBuilder = new StringBuilder();
        for (var i = 0; i < valIds.Count; i++)
        {
            reasonBuilder.Append(ReasonMap[valIds[i]]);

            if (i < valIds.Count - 1) reasonBuilder.Append(" | ");
        }

        var reason = reasonBuilder.ToString();

        await message.DeleteAsync();
        return reason;
    }

    public static Task<string?> WarnReasonSelector(CommandContext ctx)
    {
        return ReasonSelector(ctx, "warnreasons", "Verwarnen");
    }

    public static Task<string?> BanReasonSelector(CommandContext ctx)
    {
        return ReasonSelector(ctx, "banreasons", "Ban");
    }

    public static string GetUnbanURL()
    {
        string unbanurl;
        try
        {
            unbanurl = BotConfig.GetConfig()["ServerConfig"]["BanAppealURL"];
        }
        catch
        {
            unbanurl = "";
        }

        return unbanurl;
    }
}