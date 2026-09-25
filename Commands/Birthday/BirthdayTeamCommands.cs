#region

using AGC_Management.Attributes;
using AGC_Management.Services;
using DisCatSharp.ApplicationCommands;
using DisCatSharp.ApplicationCommands.Attributes;
using DisCatSharp.ApplicationCommands.Context;

#endregion

namespace AGC_Management.Commands.Birthday;

[SlashCommandGroup("birthdayteam", "Geburtstage von Mitgliedern verwalten", (long)Permissions.ModerateMembers)]
public class BirthdayTeamCommands : ApplicationCommandsModule
{
    [ACRequireStaffRole]
    [SlashCommand("set", "Setzt oder ändert den Geburtstag eines Mitglieds.")]
    public static async Task Set(InteractionContext ctx,
        [Option("user", "Das Mitglied.")] DiscordUser user,
        [Option("datum", "Geburtstag im Format TT.MM, z.B. 21.05")]
        string datum)
    {
        if (await BirthdayService.RejectIfDisabledAsync(ctx.Interaction)) return;

        if (!BirthdayService.TryParseDate(datum, out var date))
        {
            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent("Ungültiges Datum. Bitte nutze das Format `TT.MM`, z.B. `21.05`.").AsEphemeral());
            return;
        }

        var previous = await BirthdayService.GetAsync(user.Id);
        await BirthdayService.UpsertAsync(user.Id, date);
        if (previous is not null && previous.Date != date) await BirthdayService.RevokeRoleAsync(user.Id);
        await BirthdayService.GrantRoleIfTodayAsync(user.Id, date);

        var embed = new DiscordEmbedBuilder()
            .WithTitle("Geburtstag gesetzt")
            .WithDescription(previous is null
                ? $"Der Geburtstag von {user.Mention} wurde auf **{BirthdayService.Humanize(date)}** gesetzt."
                : $"Der Geburtstag von {user.Mention} wurde von **{BirthdayService.Humanize(previous.Date)}** auf **{BirthdayService.Humanize(date)}** geändert.")
            .WithColor(BotConfig.GetEmbedColor())
            .WithFooter($"Ausgeführt von {ctx.User.UsernameWithGlobalName}", ctx.User.AvatarUrl);

        await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().AddEmbed(embed).AsEphemeral());
    }

    [ACRequireStaffRole]
    [SlashCommand("delete", "Entfernt den Geburtstag eines Mitglieds.")]
    public static async Task Delete(InteractionContext ctx,
        [Option("user", "Das Mitglied.")] DiscordUser user)
    {
        if (await BirthdayService.RejectIfDisabledAsync(ctx.Interaction)) return;

        var removed = await BirthdayService.DeleteAsync(user.Id);
        if (removed) await BirthdayService.RevokeRoleAsync(user.Id);

        var embed = new DiscordEmbedBuilder()
            .WithTitle("Löschung von Geburtstag")
            .WithDescription(removed
                ? $"Der Geburtstag von {user.Mention} wurde entfernt."
                : $"{user.Mention} hat keinen Geburtstag eingetragen.")
            .WithColor(BotConfig.GetEmbedColor())
            .WithFooter($"Ausgeführt von {ctx.User.UsernameWithGlobalName}", ctx.User.AvatarUrl);

        await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().AddEmbed(embed).AsEphemeral());
    }
}
