#region

using AGC_Management.Attributes;
using AGC_Management.Providers;
using AGC_Management.Services;
using AGC_Management.Utils;
using DisCatSharp.ApplicationCommands;
using DisCatSharp.ApplicationCommands.Attributes;
using DisCatSharp.ApplicationCommands.Context;
using DisCatSharp.Exceptions;
using DisCatSharp.Interactivity.Extensions;

#endregion

namespace AGC_Management.Commands.Entbannung;

public sealed class UnbanGuildModerationCommands : ApplicationCommandsModule
{
    [ApplicationCommandRequireUnbanTeam]
    [SlashCommand("checkuser", "Überprüft den Nutzer.")]
    public static async Task CheckUserCommand(InteractionContext ctx,
        [Option("user", "Der User, der überprüft werden soll.")]
        DiscordUser user)
    {
        await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource);

        var mainGuild = CurrentApplication.TargetGuild;
        DiscordBan? banentry;
        try
        {
            banentry = await mainGuild.GetBanAsync(user.Id);
        }
        catch (NotFoundException)
        {
            banentry = null;
        }

        var (_, reports) = await ToolSet.GetBannSystemEntries(user.Id);
        var bsStatus = ToolSet.HasActiveBannSystemReport(reports);

        var antragshistorie = await EntbannungService.GetAntragshistorieAsync(user.Id);
        var description = antragshistorie.Count == 0
            ? "-- Keine Anträge gefunden --"
            : string.Concat(antragshistorie.Select(antrag =>
                $"{EntbannungService.BoolToEmoji(antrag.Entbannt)} - ``{antrag.AntragsId}`` - ``{antrag.Reason}`` - <@{antrag.ModId}> ({antrag.ModId}) - <t:{antrag.Timestamp}:f> (<t:{antrag.Timestamp}:R>) \n"));

        var embed = new DiscordEmbedBuilder()
            .WithAuthor(user.Username, iconUrl: user.AvatarUrl)
            .WithThumbnail(user.AvatarUrl)
            .AddField(new DiscordEmbedField("User-ID", user.Id.ToString(), true));

        if (banentry is null)
        {
            var memberInfo = mainGuild.Members.TryGetValue(user.Id, out var member)
                ? "Ja, seit " + member.JoinedAt.Timestamp()
                : "Nein";
            embed.WithTitle("Der User ist nicht gebannt.")
                .WithDescription($"**Nutzer auf AGC:** {memberInfo}\n**Antragshistorie:** \n{description}")
                .WithColor(DiscordColor.Green);
        }
        else
        {
            embed.WithTitle("Der User ist gebannt.")
                .WithDescription(
                    $"Grund: ```{banentry.Reason ?? "Kein Grund angegeben."}``` \n**Antragshistorie:** \n{description}")
                .WithColor(DiscordColor.Red);
        }

        if (bsStatus)
            embed.AddField(new DiscordEmbedField("Bannsystem",
                "Der User hat aktive Bannsystem-Banns. Bitte prüfen! -> <#1181260689474605147> / <#948314117192679434>"));

        embed.WithTimestamp(DateTimeOffset.Now);
        embed.WithFooter("Bericht angefordert von " + ctx.User.Username, ctx.User.AvatarUrl);

        await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));
    }

    [ApplicationCommandRequirePermissions(Permissions.Administrator)]
    [SlashCommand("ban", "Bannt einen User vom Server")]
    public static async Task BanCommand(InteractionContext ctx,
        [Option("user", "Der User, der gebannt werden soll.")]
        DiscordUser user,
        [Option("Grund", "Der Grund für den Ban")]
        string reason)
    {
        var caseid = EntbannungService.GenerateCaseId();
        var reasonString =
            $"Grund: {reason} | Von Moderator: {ctx.User.UsernameWithDiscriminator} | Datum: {DateTime.Now:dd.MM.yyyy - HH:mm}";
        var confirmEmbedBuilder = new DiscordEmbedBuilder()
            .WithTitle("Überprüfe deine Eingabe | Aktion: Ban")
            .WithFooter(ctx.User.UsernameWithDiscriminator, ctx.User.AvatarUrl)
            .WithDescription("Bitte überprüfe deine Eingabe und bestätige mit ✅ um fortzufahren.\n\n" +
                             "__Users:__\n" +
                             $"```{user.UsernameWithDiscriminator}```\n__Grund:__```{reason}```")
            .WithColor(BotConfig.GetEmbedColor());
        List<DiscordButtonComponent> buttons =
        [
            new(ButtonStyle.Secondary, $"ban_accept_{caseid}", "✅"),
            new(ButtonStyle.Secondary, $"ban_deny_{caseid}", "❌")
        ];
        await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().AddEmbed(confirmEmbedBuilder).AddComponents(buttons));
        var message = await ctx.GetOriginalResponseAsync();
        var interaction = await ctx.Client.GetInteractivity()
            .WaitForButtonAsync(message, ctx.User, TimeSpan.FromSeconds(60));
        buttons.ForEach(x => x.Disable());

        if (interaction.TimedOut || interaction.Result.Id == $"ban_deny_{caseid}")
        {
            if (!interaction.TimedOut)
                await interaction.Result.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);
            var grund = interaction.TimedOut ? "Zeitüberschreitung" : "Abgebrochen";
            await message.ModifyAsync(new DiscordMessageBuilder().AddEmbed(confirmEmbedBuilder
                .WithTitle("Ban abgebrochen")
                .WithDescription($"Der Ban wurde abgebrochen.\n\nGrund: {grund}. <:counting_warning:962007085426556989>")
                .WithColor(DiscordColor.Red).Build()));
            return;
        }

        await interaction.Result.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);
        await message.ModifyAsync(new DiscordMessageBuilder()
            .AddEmbed(new DiscordEmbedBuilder()
                .WithTitle("Ban wird bearbeitet")
                .WithFooter(ctx.User.UsernameWithDiscriminator, ctx.User.AvatarUrl)
                .WithDescription("Der Ban wird bearbeitet. Bitte warten...")
                .WithColor(DiscordColor.Yellow))
            .AddComponents(buttons));

        DiscordMessage? dm = null;
        try
        {
            dm = await user.SendMessageAsync(new DiscordEmbedBuilder()
                .WithTitle($"Du wurdest von {ctx.Guild.Name} gebannt!")
                .WithDescription($"**Begründung:**```{reason}```\n")
                .WithColor(DiscordColor.Red));
        }
        catch
        {
        }

        string result;
        DiscordColor color;
        try
        {
            await ctx.Guild.BanMemberAsync(user.Id, 0, reasonString);
            result = "Der Ban wurde erfolgreich abgeschlossen.\n" +
                     $"__Grund:__ ```{reason}```\n" +
                     "__Gebannte User:__\n" +
                     $"```{user.UsernameWithDiscriminator} | DM: {(dm is not null ? "✅" : "❌")}\n```";
            color = DiscordColor.Green;
        }
        catch (UnauthorizedException)
        {
            result = "Der Ban war nicht erfolgreich.\n" +
                     $"__Grund:__ ```{reason}```\n" +
                     "__Nicht gebannte User:__\n" +
                     $"```{user.UsernameWithDiscriminator}\n```";
            color = DiscordColor.Red;
            if (dm is not null)
                try
                {
                    await dm.DeleteAsync();
                }
                catch
                {
                }
        }

        await message.ModifyAsync(new DiscordMessageBuilder().AddEmbed(new DiscordEmbedBuilder()
            .WithTitle("Ban abgeschlossen")
            .WithDescription(result)
            .WithFooter(ctx.User.UsernameWithDiscriminator, ctx.User.AvatarUrl)
            .WithColor(color)));
    }

    [ApplicationCommandRequirePermissions(Permissions.Administrator)]
    [SlashCommand("revokeban", "Entbannt einen User von AGC.")]
    public static async Task RevokeBanCommand(InteractionContext ctx,
        [Option("user", "Der User, der entbannt werden soll.")]
        DiscordUser user,
        [Option("antragsnummer", "Die Antragsnummer."), MinimumLength(4), MaximumLength(4)]
        string antragsnummer,
        [Autocomplete(typeof(RevokeBanAutocompleteProvider))] [Option("Grund", "Der Grund für die Entbannung", true)]
        string reason)
    {
        var mainGuild = CurrentApplication.TargetGuild;
        DiscordBan banentry;
        try
        {
            banentry = await mainGuild.GetBanAsync(user.Id);
        }
        catch (NotFoundException)
        {
            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().WithContent($"Der User ``{user.Id}`` ist nicht gebannt."));
            return;
        }

        var channels = await ctx.Guild.GetChannelsAsync();
        var channel = channels.FirstOrDefault(x => x.Name.Contains(antragsnummer));
        if (channel is null)
        {
            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().WithContent(
                    $"Es wurde kein Antrag mit der Antragsnummer ``{antragsnummer}`` gefunden."));
            return;
        }

        if (await EntbannungService.GetAbstimmungByChannelAsync(channel.Id) is not null)
        {
            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().WithContent(
                    $"Der Antrag mit der Antragsnummer ``{antragsnummer}`` ist noch in Abstimmung. Bitte warte bis die Abstimmung beendet ist."));
            return;
        }

        var caseid = EntbannungService.GenerateCaseId();
        var confirmEmbedBuilder = new DiscordEmbedBuilder()
            .WithTitle("Überprüfe deine Eingabe | Aktion: Entbannung")
            .WithFooter(ctx.User.UsernameWithDiscriminator, ctx.User.AvatarUrl)
            .WithDescription("Bitte überprüfe deine Eingabe und bestätige mit ✅ um fortzufahren.\n\n" +
                             "__Users:__\n" +
                             $"```{user.UsernameWithDiscriminator}```\n__Grund:__```{reason}```")
            .WithColor(BotConfig.GetEmbedColor());
        List<DiscordButtonComponent> buttons =
        [
            new(ButtonStyle.Secondary, $"unban_accept_{caseid}", "✅"),
            new(ButtonStyle.Secondary, $"unban_deny_{caseid}", "❌")
        ];
        await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().AddEmbed(confirmEmbedBuilder).AddComponents(buttons));
        var msg = await ctx.GetOriginalResponseAsync();
        var result = await ctx.Client.GetInteractivity().WaitForButtonAsync(msg, ctx.User, TimeSpan.FromSeconds(300));
        buttons.ForEach(x => x.Disable());
        await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(confirmEmbedBuilder).AddComponents(buttons));

        if (result.TimedOut || result.Result.Id != $"unban_accept_{caseid}")
        {
            var grund = result.TimedOut ? "Zeitüberschreitung" : "Abgebrochen vom ausführer!";
            confirmEmbedBuilder.WithTitle("Entbannung abgebrochen")
                .WithDescription($"Die Entbannung wurde abgebrochen.\n\nGrund: {grund} ⚠️")
                .WithColor(DiscordColor.Red);
            await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(confirmEmbedBuilder));
            return;
        }

        confirmEmbedBuilder.WithTitle("Entbannung bestätigt")
            .WithDescription("Die Entbannung wird durchgeführt.")
            .WithColor(DiscordColor.Green);
        await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(confirmEmbedBuilder));

        await mainGuild.UnbanMemberAsync(user.Id, reason);
        var flagstring =
            $"Durch Antrag entbannt. \nUrsprünglicher Banngrund: ``{banentry.Reason ?? "Kein Grund angegeben."}`` \n" +
            $"__Details:__\n Entbanngrund: ``{reason}``\n Antrags-ID: ``{antragsnummer}``\n Entbannungszeitpunkt: {DateTimeOffset.Now.Timestamp()}\n Entbannung ausgeführt von: ``{ctx.User.UsernameWithDiscriminator}`` ({ctx.User.Id})";
        await EntbannungService.AddFlagAsync(user.Id, ctx.Client.CurrentUser.Id, flagstring, caseid);

        await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(new DiscordEmbedBuilder()
            .WithTitle("Entbannung erfolgreich")
            .WithFooter(ctx.User.UsernameWithDiscriminator, ctx.User.AvatarUrl)
            .WithDescription(
                $"Der User ``{user.UsernameWithDiscriminator}`` ``{user.Id}`` wurde erfolgreich entbannt. \n" +
                $"Grund: ```{reason}```")
            .WithColor(DiscordColor.Green)));
    }
}
