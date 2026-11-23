#region

using System.Collections.Concurrent;
using AGC_Management.Entities.Entbannung;
using AGC_Management.Services;
using AGC_Management.Tasks.Entbannung;
using AGC_Management.Utils;
using DisCatSharp.Exceptions;

#endregion

namespace AGC_Management.Eventlistener.Entbannung;

[EventHandler]
public class EntbannungInteractionListener : BaseCommandModule
{
    private static readonly ConcurrentDictionary<ulong, long> NotesOpenedAt = new();

    [Event]
    public static Task ComponentInteractionCreated(DiscordClient client, ComponentInteractionCreateEventArgs e)
    {
        if (!EntbannungConfig.IsUnbanGuild(e.Guild)) return Task.CompletedTask;

        var cid = e.Interaction.Data.CustomId;
        Func<Task>? handler = cid switch
        {
            _ when cid.StartsWith("vote_yes_") || cid.StartsWith("vote_no_") => () => HandleVote(client, e, cid),
            "open_appealticketinfo" => () => HandleOpenInfo(client, e),
            "open_appealticket_confirm" => () => HandleConfirm(client, e),
            "open_sperrinfo" => () => HandleSperrInfo(client, e),
            _ => null
        };
        if (handler is null) return Task.CompletedTask;

        _ = Task.Run(async () =>
        {
            try
            {
                await handler();
            }
            catch (Exception ex)
            {
                await ErrorReporting.SendErrorToDev(client, e.User, ex);
            }
        });
        return Task.CompletedTask;
    }

    private static async Task HandleVote(DiscordClient client, ComponentInteractionCreateEventArgs e, string cid)
    {
        await e.Interaction.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().AsEphemeral());
        try
        {
            if (await EntbannungService.GetAbstimmungByMessageAsync(e.Message.Id) is null)
            {
                await e.Interaction.EditOriginalResponseAsync(
                    new DiscordWebhookBuilder().WithContent("Die Abstimmung wurde nicht gefunden."));
                return;
            }

            var existingVote = await EntbannungService.GetUserVoteAsync(e.Message.Id, e.User.Id);
            if (existingVote is not null)
            {
                await EntbannungService.RemoveVoteAsync(client, e.Message.Id, e.User.Id, existingVote.Value);
                await e.Interaction.EditOriginalResponseAsync(
                    new DiscordWebhookBuilder().WithContent("Dein vorheriger Vote wurde entfernt."));
            }
            else
            {
                var positive = cid.StartsWith("vote_yes_");
                await EntbannungService.AddVoteAsync(client, e.Message.Id, e.User.Id, positive);
                await e.Interaction.EditOriginalResponseAsync(
                    new DiscordWebhookBuilder().WithContent(
                        $"Dein Vote wurde gezählt! Stimme: **{(positive ? "Ja" : "Nein")}**"));
            }

            await EntbannungTasks.UpdateVoteMessageAsync(client, e.Message.Id);
        }
        catch (Exception)
        {
            await e.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().AddEmbed(ErrorEmbed()));
            throw;
        }
    }

    private static async Task HandleOpenInfo(DiscordClient client, ComponentInteractionCreateEventArgs e)
    {
        await e.Interaction.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().AsEphemeral());

        var banreason = "";
        var isBanned = false;
        try
        {
            await e.Interaction.EditOriginalResponseAsync(
                new DiscordWebhookBuilder().WithContent("Prüfe, ob du gebannt bist..."));
            var ban = await CurrentApplication.TargetGuild.GetBanAsync(e.User.Id);
            banreason = ban.Reason ?? "Kein Grund angegeben.";
            await Task.Delay(1000);
            isBanned = true;
            await e.Interaction.EditOriginalResponseAsync(
                new DiscordWebhookBuilder().WithContent("Du bist gebannt! Setze fort..."));
        }
        catch (NotFoundException)
        {
            await e.Interaction.EditOriginalResponseAsync(
                new DiscordWebhookBuilder().WithContent("Du bist nicht gebannt! Breche ab..."));
            await Task.Delay(500);
        }
        catch (Exception)
        {
            await e.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().AddEmbed(ErrorEmbed()));
            throw;
        }

        var (_, reports) = await ToolSet.GetBannSystemEntries(e.User.Id);
        if (ToolSet.HasActiveBannSystemReport(reports))
        {
            await e.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().AddEmbed(BannSystemEmbed(
                " Nachdem dein Bann aufgehoben wurde, kannst du - wenn nicht Entbannt - einen Entbannungsantrag stellen.")));
            await LogAsync(client,
                $"{e.User.Mention} ({e.User.Id}) hat die Antragshinweise **geöffnet** (BANNSYSTEM GEBANNT | DB Check)");
            return;
        }

        if (await EntbannungService.IsPermaBlockedAsync(e.User.Id))
        {
            await e.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().AddEmbed(
                new DiscordEmbedBuilder()
                    .WithTitle("Permanent ausgeschlossen")
                    .WithDescription(
                        "Du wurdest vom Entbannungssystem permanent ausgeschlossen. Du kannst keinen Entbannungsantrag stellen. Das heißt du bist für immer von der Entbannung ausgeschlossen.")
                    .WithColor(DiscordColor.Red)));
            await LogAsync(client,
                $"{e.User.Mention} ({e.User.Id}) hat die Antragshinweise **geöffnet** (PERMANENT AUSGESCHLOSSEN)");
            return;
        }

        await e.Interaction.EditOriginalResponseAsync(
            new DiscordWebhookBuilder().WithContent("Prüfe, ob du für Anträge gesperrt bist..."));
        await Task.Delay(500);
        if (await EntbannungService.GetSperreAsync(e.User.Id) is not null)
        {
            await e.Interaction.EditOriginalResponseAsync(
                new DiscordWebhookBuilder().WithContent("Du bist für Anträge gesperrt!"));
            await Task.Delay(1000);
            await e.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().AddEmbed(
                new DiscordEmbedBuilder()
                    .WithDescription("Du bist aktuell für Anträge gesperrt. Du kannst keinen Entbannungsantrag stellen.")
                    .WithColor(DiscordColor.Red)));
            return;
        }

        await e.Interaction.EditOriginalResponseAsync(
            new DiscordWebhookBuilder().WithContent("Du bist nicht für Anträge gesperrt! Setze fort..."));

        if (e.User.Id == GlobalProperties.BotOwnerId)
        {
            await e.Interaction.EditOriginalResponseAsync(
                new DiscordWebhookBuilder().WithContent("Du bist der Botowner! Setze fort... (Test)"));
            isBanned = true;
        }

        if (!isBanned)
        {
            await e.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().AddEmbed(
                new DiscordEmbedBuilder()
                    .WithTitle("Nicht gebannt!")
                    .WithDescription(
                        "Wie es scheint, bist du nicht auf AGC gebannt. Diese Überprüfung ist automatisiert. Du kannst also keinen Entbannungsantrag stellen.")
                    .WithColor(DiscordColor.Red)));
            return;
        }

        if (banreason.Contains("bannsystem | report-id:", StringComparison.OrdinalIgnoreCase))
        {
            await e.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().AddEmbed(BannSystemEmbed("")));
            await LogAsync(client,
                $"{e.User.Mention} ({e.User.Id}) hat die Antragshinweise **geöffnet** (BANNSYSTEM GEBANNT | Reason String check)");
            return;
        }

        NotesOpenedAt[e.User.Id] = DateTimeOffset.Now.ToUnixTimeSeconds();

        var member = await e.Guild.GetMemberAsync(e.User.Id, true);
        var rb = new DiscordWebhookBuilder();
        if (member.Roles.All(r => r.Id != EntbannungConfig.AppealRoleId))
            rb.AddComponents(new DiscordButtonComponent(ButtonStyle.Success, "open_appealticket_confirm",
                "Ich habe alles gelesen und verstanden!", emoji: new DiscordComponentEmoji("✅")));
        rb.AddEmbeds(EntbannungMessages.UnbanNotes());
        await e.Interaction.EditOriginalResponseAsync(rb);
        await LogAsync(client, $"{e.User.Mention} ({e.User.Id}) hat die Antragshinweise **geöffnet**");
    }

    private static async Task HandleConfirm(DiscordClient client, ComponentInteractionCreateEventArgs e)
    {
        long? timediff = NotesOpenedAt.TryGetValue(e.User.Id, out var start)
            ? DateTimeOffset.Now.ToUnixTimeSeconds() - start
            : null;
        var tookseconds = timediff switch
        {
            null => "N/A",
            < 15 => $"⚠️ {timediff} Sekunden",
            _ => $"{timediff} Sekunden"
        };

        var member = await e.Guild.GetMemberAsync(e.User.Id);
        var hasAppealRole = member.Roles.Any(r => r.Id == EntbannungConfig.AppealRoleId);

        var bypassReadCheck = false;
        try
        {
            var ban = await CurrentApplication.TargetGuild.GetBanAsync(e.User.Id);
            var reason = (ban.Reason ?? "").ToLower();
            bypassReadCheck = reason.Contains("scam") || reason.Contains("entbannung");
        }
        catch (NotFoundException)
        {
        }

        if ((timediff ?? 0) < 15 && !hasAppealRole && !bypassReadCheck)
        {
            await e.Interaction.CreateResponseAsync(InteractionResponseType.UpdateMessage,
                new DiscordInteractionResponseBuilder().AddEmbed(new DiscordEmbedBuilder()
                    .WithTitle("Antrag abgelehnt!")
                    .WithDescription(
                        "Da du die Antragshinweise nicht gelesen hast und einfach nur auf den Button geklickt hast, wurde dein Antrag automatisch abgelehnt. Du kannst es in 3 Monaten erneut versuchen.")
                    .WithColor(DiscordColor.Red)));
            await LogAsync(client,
                $"{e.User.Mention} ({e.User.Id}) hat die Antragshinweise **nicht gelesen** | Automatische ablehnung erfolgt - {Now()} | Zeit benötigt: {tookseconds}",
                false);
            await AutoDenyAsync(client, e.Guild, e.User, "Hinweise nicht gelesen");
            return;
        }

        await LogAsync(client,
            $"{e.User.Mention} ({e.User.Id}) hat die Antragshinweise **akzeptiert** - {Now()} | Zeit benötigt: {tookseconds}",
            false);
        await e.Interaction.CreateResponseAsync(InteractionResponseType.UpdateMessage,
            new DiscordInteractionResponseBuilder().WithContent("Hinweise wurden akzeptiert. Fahre fort..."));
        await Task.Delay(2000);
        await e.Interaction.EditOriginalResponseAsync(
            new DiscordWebhookBuilder().WithContent("Ticket wird erstellt..."));
        await Task.Delay(1000);
        await e.Interaction.EditOriginalResponseAsync(
            new DiscordWebhookBuilder().WithContent("Prüfe auf offenes Ticket..."));

        if (hasAppealRole)
        {
            await e.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().AddEmbed(
                new DiscordEmbedBuilder()
                    .WithTitle("Fehler!")
                    .WithDescription(
                        "Du hast bereits ein offenes Ticket. Bitte nutze dieses, um einen Entbannungsantrag zu stellen.")
                    .WithColor(DiscordColor.Red)));
            return;
        }

        await Task.Delay(1000);
        await e.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().WithContent("Erstelle Ticket..."));
        var logChannel = await client.GetChannelAsync(EntbannungConfig.LogChannelId);
        await logChannel.SendMessageAsync($"$new {e.User.Id}");
        await Task.Delay(500);
        await e.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().WithContent("Ticket erstellt!"));
    }

    private static async Task HandleSperrInfo(DiscordClient client, ComponentInteractionCreateEventArgs e)
    {
        await e.Interaction.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().AsEphemeral());
        await LogAsync(client, $"{e.User.Mention} ({e.User.Id}) hat die Sperrzeit **abgefragt**");

        var sperre = await EntbannungService.GetSperreAsync(e.User.Id);
        if (sperre is null)
        {
            await e.Interaction.EditOriginalResponseAsync(
                new DiscordWebhookBuilder().WithContent("Du bist nicht gesperrt!"));
            return;
        }

        var expiresAt = sperre.ExpiresAt;
        var secondsLeft = expiresAt - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var humanTime = TimeSpan.FromSeconds(secondsLeft);
        await e.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().WithContent(
            $"Du bist für einen Antrag gesperrt. Deine Sperre läuft bis <t:{expiresAt}:f> - ( <t:{expiresAt}:R> )"));
        await LogAsync(client,
            $"🕒 **Sperrzeit verbleibend für {e.User.Username} ({e.User.Id})**: {humanTime.Days} Tage, {humanTime.Hours} Stunden, {humanTime.Minutes} Minuten ({secondsLeft} Sekunden)",
            false);
    }

    private static async Task AutoDenyAsync(DiscordClient client, DiscordGuild guild, DiscordUser user,
        string reason)
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMonths(3).ToUnixTimeSeconds();
        await EntbannungService.AddSperreAsync(user.Id, reason, expiresAt);

        try
        {
            var member = await guild.GetMemberAsync(user.Id);
            await member.GrantRoleAsync(guild.GetRole(EntbannungConfig.SperreRoleId), "Antragssperre");
        }
        catch
        {
        }

        var bot = client.CurrentUser;
        await guild.GetChannel(EntbannungConfig.SperreInfoChannelId).SendMessageAsync(new DiscordEmbedBuilder()
            .WithDescription(
                $"{DateTime.UtcNow.Timestamp(TimestampFormat.FullDateShortTime)} - {user.Mention} ({user.Id}) - Antrag -/- (Autosperre) - ``{reason}`` -> Gesperrt bis: <t:{expiresAt}:f> ( <t:{expiresAt}:R> )")
            .WithFooter($"Gesperrt durch {bot.UsernameWithDiscriminator} ({bot.Id})", bot.AvatarUrl));

        if (!await EntbannungService.AntragExistsAsync("STUB0000", user.Id))
            await EntbannungService.AddAntragAsync(new AntragsverlaufEntry(user.Id, "STUB0000", false, reason, bot.Id,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        await EntbannungService.PostHistoryEmbedAsync(client, false, bot, "-/-", user, reason);
    }

    private static async Task LogAsync(DiscordClient client, string message, bool appendTimestamp = true)
    {
        try
        {
            var logChannel = await client.GetChannelAsync(EntbannungConfig.LogChannelId);
            await logChannel.SendMessageAsync(appendTimestamp ? $"{message} - {Now()}" : message);
        }
        catch (Exception ex)
        {
            CurrentApplication.Logger.Error(ex, "Entbannung: Log-Nachricht konnte nicht gesendet werden");
        }
    }

    private static string Now()
    {
        return DateTime.Now.Timestamp(TimestampFormat.LongDateShortTime);
    }

    private static DiscordEmbed ErrorEmbed()
    {
        return new DiscordEmbedBuilder()
            .WithTitle("Fehler!")
            .WithDescription(
                "Es ist ein Fehler aufgetreten. Bitte versuche es später erneut. Der Fehler wurde automatisch an den Entwickler weitergeleitet.")
            .WithColor(DiscordColor.Red)
            .Build();
    }

    private static DiscordEmbed BannSystemEmbed(string suffix)
    {
        return new DiscordEmbedBuilder()
            .WithTitle("Bannsystem")
            .WithDescription(
                "Du wurdest vom globalen Bannsystem gebannt. Du kannst hier keinen Entbannungsantrag stellen. \n\n" +
                "Bitte wende dich an [Bannsystem Support](https://bannsystem.de) um deinen Bann zu klären. Dein Bann betrifft nicht nur AGC, sondern alle Server, die das Bannsystem nutzen." +
                suffix)
            .WithColor(DiscordColor.Red)
            .Build();
    }
}
