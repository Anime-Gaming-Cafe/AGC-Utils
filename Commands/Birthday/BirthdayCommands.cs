#region

using AGC_Management.Services;
using DisCatSharp.ApplicationCommands;
using DisCatSharp.ApplicationCommands.Attributes;
using DisCatSharp.ApplicationCommands.Context;

#endregion

namespace AGC_Management.Commands.Birthday;

[SlashCommandGroup("birthday", "Geburtstagssystem")]
public class BirthdayCommands : ApplicationCommandsModule
{
    private const string MonthListThumbnail =
        "https://cdn.discordapp.com/attachments/764921088689438771/924729553023299634/b24070c40a5ebb9a9c50dc8c2849bd9a.png";

    [SlashCommand("check", "Zeigt den Geburtstag eines Mitglieds an.")]
    public static async Task Check(InteractionContext ctx,
        [Option("user", "Das Mitglied, dessen Geburtstag angezeigt werden soll.")]
        DiscordUser user)
    {
        if (await BirthdayService.RejectIfDisabledAsync(ctx.Interaction)) return;

        var entry = await BirthdayService.GetAsync(user.Id);
        var embed = new DiscordEmbedBuilder()
            .WithTitle("Abfrage - Geburtstag")
            .WithColor(BotConfig.GetEmbedColor())
            .WithDescription(entry is null
                ? $"{user.UsernameWithGlobalName} hat den Geburtstag **nicht** eingetragen.\n\n{user.UsernameWithGlobalName} kann den Geburtstag aber mit `/birthdaysetup` eintragen."
                : $"{user.UsernameWithGlobalName} hat am **{BirthdayService.Humanize(entry.Date)}** Geburtstag.")
            .WithFooter($"Ausgeführt von {ctx.User.UsernameWithGlobalName}", ctx.User.AvatarUrl);

        await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().AddEmbed(embed));
    }

    [SlashCommand("monthlist", "Zeigt, wie viele Mitglieder in welchem Monat Geburtstag haben.")]
    public static async Task MonthList(InteractionContext ctx)
    {
        if (await BirthdayService.RejectIfDisabledAsync(ctx.Interaction)) return;

        var dates = await BirthdayService.GetAllDatesAsync();
        var perMonth = dates.GroupBy(BirthdayService.MonthOf).ToDictionary(g => g.Key, g => g.Count());
        var culture = new System.Globalization.CultureInfo("de-DE");

        var embed = new DiscordEmbedBuilder()
            .WithTitle("🗓️ Aufzählung nach Monaten - Geburtstagssystem")
            .WithDescription("Dies ist eine Aufzählung, wie viele Personen in welchem Monat Geburtstag haben.\n" +
                             $"Aktuell sind **{dates.Count}** User im System registriert.")
            .WithColor(BotConfig.GetEmbedColor())
            .WithThumbnail(MonthListThumbnail)
            .WithFooter($"Ausgeführt von {ctx.User.UsernameWithGlobalName}", ctx.User.AvatarUrl);

        for (var month = 1; month <= 12; month++)
            embed.AddField(new DiscordEmbedField(culture.DateTimeFormat.GetMonthName(month),
                perMonth.GetValueOrDefault(month).ToString(), true));

        await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().AddEmbed(embed));
    }
}
