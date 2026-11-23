#region

using AGC_Management.Attributes;
using AGC_Management.Providers;
using AGC_Management.Services;
using AGC_Management.Utils;
using DisCatSharp.ApplicationCommands;
using DisCatSharp.ApplicationCommands.Attributes;
using DisCatSharp.ApplicationCommands.Context;

#endregion

namespace AGC_Management.Commands.Entbannung;

public sealed class SperreCommands : ApplicationCommandsModule
{
    [ApplicationCommandRequireUnbanTeam]
    [SlashCommand("sperre", "Sperrt einen User von der Antragstellung für eine bestimmte Zeit.")]
    public static async Task SperreCommand(InteractionContext ctx,
        [Option("user", "Der User, der gesperrt werden soll.")]
        DiscordUser user,
        [Option("antragsnummer", "Die Antragsnummer."), MinimumLength(4), MaximumLength(4)]
        string antragsnummer,
        [Autocomplete(typeof(SperreAutocompleteProvider))] [Option("grund", "Der Grund für die Sperre.", true)]
        string reason)
    {
        await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().AsEphemeral());

        await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent("Prüfe Eingaben..."));

        if (!antragsnummer.All(char.IsDigit))
        {
            await ctx.EditResponseAsync(
                new DiscordWebhookBuilder().WithContent("⚠️ Die Antragsnummer ist ungültig!"));
            return;
        }

        await ctx.EditResponseAsync(
            new DiscordWebhookBuilder().WithContent("Prüfe, ob der User bereits gesperrt ist..."));
        if (await EntbannungService.GetSperreAsync(user.Id) is not null)
        {
            await ctx.EditResponseAsync(
                new DiscordWebhookBuilder().WithContent("⚠️ Der User ist bereits gesperrt!"));
            return;
        }

        await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
            "Der User ist nicht gesperrt. Sperre den User für die angegebene Zeit..."));
        await Task.Delay(1000);
        try
        {
            var member = await ctx.Guild.GetMemberAsync(user.Id);
            await member.GrantRoleAsync(ctx.Guild.GetRole(EntbannungConfig.SperreRoleId), "Antragssperre");
            await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent("✅ Der User wurde gesperrt!"));
        }
        catch (Exception e)
        {
            CurrentApplication.Logger.Warning(e, "Entbannung: Sperre-Rolle konnte nicht vergeben werden");
        }

        var expiresAt = DateTimeOffset.UtcNow.AddMonths(3).ToUnixTimeSeconds();
        await EntbannungService.AddSperreAsync(user.Id, reason, expiresAt);
        await ctx.EditResponseAsync(
            new DiscordWebhookBuilder().WithContent("✅ Die Sperre wurde eingetragen!"));

        var embed = new DiscordEmbedBuilder()
            .WithDescription(
                $"{DateTime.UtcNow.Timestamp(TimestampFormat.FullDateShortTime)} - {user.Mention} ({user.Id}) - Antrag {antragsnummer} - ``{reason}`` -> Gesperrt bis: <t:{expiresAt}:f> ( <t:{expiresAt}:R> )")
            .WithFooter($"Gesperrt durch {ctx.User.UsernameWithDiscriminator} ({ctx.User.Id})", ctx.User.AvatarUrl);
        await ctx.Guild.GetChannel(EntbannungConfig.SperreInfoChannelId).SendMessageAsync(embed);

        await EntbannungService.TryAddAntragsverlaufAsync(ctx.Client, false, ctx.User, antragsnummer, user, reason);
    }

    [ApplicationCommandRequireUnbanTeam]
    [ApplicationCommandRequireGuildOwner]
    [SlashCommand("removeantragssperre", "Entfernt die Antragssperre für einen User.")]
    public static async Task RemoveSperreCommand(InteractionContext ctx,
        [Option("user", "Der User, dessen Antragssperre entfernt werden soll.")]
        DiscordUser user)
    {
        await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().AsEphemeral());

        await ctx.EditResponseAsync(
            new DiscordWebhookBuilder().WithContent("Prüfe, ob bereits eine Antragssperre existiert..."));
        if (await EntbannungService.GetSperreAsync(user.Id) is null)
        {
            await ctx.EditResponseAsync(
                new DiscordWebhookBuilder().WithContent("Es existiert keine Antragssperre für diesen User!"));
            return;
        }

        await ctx.EditResponseAsync(
            new DiscordWebhookBuilder().WithContent("Antragssperre gefunden. Entferne die Sperre..."));
        await EntbannungService.ExpireSperreAsync(user.Id);
        await ctx.EditResponseAsync(
            new DiscordWebhookBuilder().WithContent(
                "Die Antragssperre wurde erfolgreich entfernt! Der User wird innerhalb der nächsten 5 Minuten automatisch freigegeben."));
    }

    [ApplicationCommandRequireUnbanTeam]
    [SlashCommand("disableautomations", "Schaltet die 24h-Auto-Aktionen für dieses Ticket ab oder wieder an.")]
    public static async Task DisableAutomationsCommand(InteractionContext ctx)
    {
        await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().AsEphemeral());

        if (!ctx.Channel.Name.StartsWith("antrag-"))
        {
            await ctx.EditResponseAsync(
                new DiscordWebhookBuilder().WithContent("⚠️ Dieser Befehl funktioniert nur in Antrag-Channels."));
            return;
        }

        var disabled = await EntbannungService.ToggleAutomationAsync(ctx.Channel.Id, ctx.User.Id);
        await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent(disabled
            ? "✅ Automatisierungen für dieses Ticket **deaktiviert**. Der Watcher überspringt dieses Ticket."
            : "✅ Automatisierungen für dieses Ticket wieder **aktiviert**."));

        try
        {
            var logembed = new DiscordEmbedBuilder()
                .WithTitle(disabled ? "Automatisierungen deaktiviert" : "Automatisierungen wieder aktiviert")
                .WithDescription(
                    $"Automatisierungen für Antrag <#{ctx.Channel.Id}> (`#{ctx.Channel.Name}`) wurden durch {ctx.User.Mention} ({ctx.User.Id}) {(disabled ? "**deaktiviert**" : "**wieder aktiviert**")}.")
                .WithColor(disabled ? DiscordColor.Orange : DiscordColor.Green);
            await ctx.Guild.GetChannel(EntbannungConfig.AbstimmungsLogChannelId)
                .SendMessageAsync(new DiscordMessageBuilder().AddEmbed(logembed));
        }
        catch (Exception ex)
        {
            CurrentApplication.Logger.Error(ex, "Entbannung: /disableautomations konnte nicht geloggt werden");
        }
    }

    [ApplicationCommandRequirePermissions(Permissions.Administrator)]
    [SlashCommand("permablock", "Blockt einen User vom Entbannungssystem")]
    public static async Task PermaBlockCommand(InteractionContext ctx,
        [Option("user", "Der User, der geblockt werden soll.")]
        DiscordUser user)
    {
        var blocked = await EntbannungService.AddPermaBlockAsync(user.Id);
        await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().WithContent(blocked
                ? $"User {user.Username} wurde erfolgreich geblockt."
                : $"User {user.Username} ist bereits geblockt."));
    }
}
