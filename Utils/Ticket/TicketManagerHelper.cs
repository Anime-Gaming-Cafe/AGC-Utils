#region

using System.Diagnostics;
using System.Text;
using AGC_Management.Components;
using AGC_Management.Entities.Ticket;
using AGC_Management.Enums;
using AGC_Management.Services;
using AGC_Management.Utils;
using DisCatSharp.Interactivity.Extensions;

#endregion

namespace AGC_Management.Managers;

public class TicketManagerHelper
{
    private static readonly Random random = new();
    private static DiscordClient _client;

    public TicketManagerHelper(DiscordClient client)
    {
        _client = client;
    }

    /// <summary>
    ///     Answers a component callback that did not come from someone allowed to work this ticket. The
    ///     button that produced the menu is gated, but a custom id can be sent without ever pressing it.
    /// </summary>
    public static async Task<bool> EnsureTeamAsync(DiscordInteraction interaction)
    {
        var member = await interaction.User.ConvertToMember(interaction.Guild);
        if (await TicketAccess.MayHandleAsync(member, interaction.Channel)) return true;

        await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().WithContent("Du bist kein Teammitglied!").AsEphemeral());
        return false;
    }

    public static async Task<long> GetTicketOwnerFromChannel(DiscordChannel channel)
    {
        var newcon = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        var query = $"SELECT ticket_owner FROM ticketcache where tchannel_id = '{channel.Id}'";
        await using var cmd = newcon.CreateCommand(query);
        await using var reader = await cmd.ExecuteReaderAsync();
        long ticket_owner = 0;
        while (reader.Read()) ticket_owner = reader.GetInt64(0);

        await reader.CloseAsync();
        return ticket_owner;
    }

    public static async Task<int> GetTicketCountFromThisUser(long user_id)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        var query = $"SELECT COUNT(*) FROM ticketstore where ticket_owner = '{user_id}'";
        await using var cmd = con.CreateCommand(query);
        var rowCount = Convert.ToInt32(cmd.ExecuteScalar());
        return rowCount;
    }

    public static string GenerateTicketID(int length = 9)
    {
        const string chars = "0123456789abcdef";
        return new string([.. Enumerable.Repeat(chars, length).Select(s => s[random.Next(s.Length)])]);
    }

    /// <summary>Open tickets of a user, either overall or within one category.</summary>
    public static async Task<int> CountOpenTicketsAsync(ulong userId, string? categoryId = null)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        var sql = "SELECT COUNT(*) FROM ticketstore WHERE ticket_owner = @owner AND closed = false";
        if (categoryId is not null) sql += " AND tickettype = @category";

        await using var cmd = con.CreateCommand(sql);
        cmd.Parameters.AddWithValue("owner", (long)userId);
        if (categoryId is not null) cmd.Parameters.AddWithValue("category", categoryId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public static async Task<bool> IsOpenTicket(DiscordChannel ch)
    {
        var isTicketOpen = false;
        var newcon = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

        var query = $@"
        SELECT COUNT(*)
        FROM ticketstore
        WHERE ticket_id = (
            SELECT ticket_id
            FROM ticketcache
            WHERE tchannel_id = '{(long)ch.Id}'
        )
        AND closed = false";

        await using var cmd = newcon.CreateCommand(query);
        var rowCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());

        if (rowCount > 0) isTicketOpen = true;

        return isTicketOpen;
    }

    /// <summary>
    ///     Channel of a user's open ticket. The cache table also holds closed-but-not-yet-deleted tickets,
    ///     so the join against ticketstore is what makes this answer "open".
    /// </summary>
    public static async Task<long> GetOpenTicketChannel(long user_id, string? categoryId = null)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        var sql = "SELECT c.tchannel_id FROM ticketcache c JOIN ticketstore s ON s.ticket_id = c.ticket_id " +
                  "WHERE c.ticket_owner = @owner AND s.closed = false";
        if (categoryId is not null) sql += " AND s.tickettype = @category";
        sql += " ORDER BY s.opened_at DESC LIMIT 1";

        await using var cmd = con.CreateCommand(sql);
        cmd.Parameters.AddWithValue("owner", user_id);
        if (categoryId is not null) cmd.Parameters.AddWithValue("category", categoryId);
        var result = await cmd.ExecuteScalarAsync();
        return result is long channelId ? channelId : 0;
    }

    public static async Task Claim_UpdateHeaderComponents(ComponentInteractionCreateEventArgs interaction)
    {
        await interaction.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);
        var message = await interaction.Channel.GetMessageAsync(interaction.Message.Id);
        var mb = new DiscordMessageBuilder();
        mb.WithContent(message.Content);
        mb.AddEmbed(message.Embeds[0]);
        var components = TicketComponents.GetTicketClaimedActionRow();
        List<DiscordActionRowComponent> row =
		[
			new DiscordActionRowComponent(components)
        ];
        mb.AddComponents(row);
        await message.ModifyAsync(mb);
    }

    public static async Task<string> GetTicketIdFromChannel(DiscordChannel channel)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        var query = $"SELECT ticket_id FROM ticketcache where tchannel_id = '{channel.Id}'";
        await using var cmd = con.CreateCommand(query);
        await using var reader = await cmd.ExecuteReaderAsync();
        var ticket_id = "";
        while (reader.Read()) ticket_id = reader.GetString(0);

        await reader.CloseAsync();

        return ticket_id;
    }

    /// <summary>
    ///     Posts the ticket header and remembers its message id, so closing the ticket later edits the
    ///     right message instead of guessing at the oldest one in the channel.
    /// </summary>
    public static async Task<DiscordMessage> InsertHeaderIntoTicket(DiscordChannel ticketChannel, string ticketId,
        TicketCategory category, DiscordUser owner, string pingContent,
        IReadOnlyList<TicketIntakeAnswer>? answers = null)
    {
        var eb = new DiscordEmbedBuilder()
            .WithAuthor(owner.GetFormattedUserName(), owner.AvatarUrl)
            .WithColor(DiscordColor.Blurple)
            .WithFooter($"Nutzer-ID: {owner.Id} • Ticket-ID: {ticketId}")
            .WithDescription($"**Ticket-Typ: {category.Label}**");

        if (answers is not null)
            foreach (var answer in answers)
                eb.AddField(new DiscordEmbedField(answer.QuestionLabel.Truncate(256),
                    string.IsNullOrWhiteSpace(answer.Answer) ? "-" : answer.Answer.Truncate(1024)));

        var mb = new DiscordMessageBuilder();
        mb.WithContent(pingContent);
        mb.AddEmbed(eb.Build());
        List<DiscordActionRowComponent> row =
        [
            new DiscordActionRowComponent(TicketComponents.GetTicketActionRow())
        ];
        mb.AddComponents(row);

        var message = await ticketChannel.SendMessageAsync(mb);
        await SetHeaderMessageAsync(ticketId, message.Id);
        return message;
    }

    public static async Task SetHeaderMessageAsync(string ticketId, ulong messageId)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd =
            con.CreateCommand("UPDATE ticketcache SET header_message_id = @message WHERE ticket_id = @ticket");
        cmd.Parameters.AddWithValue("message", (long)messageId);
        cmd.Parameters.AddWithValue("ticket", ticketId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    ///     The header message of a ticket. Tickets that were opened before the id was recorded fall back to
    ///     the old guess, the oldest message in the most recent page.
    /// </summary>
    public static async Task<DiscordMessage?> GetHeaderMessageAsync(DiscordChannel channel)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd =
            con.CreateCommand("SELECT header_message_id FROM ticketcache WHERE tchannel_id = @channel LIMIT 1");
        cmd.Parameters.AddWithValue("channel", (long)channel.Id);
        var stored = await cmd.ExecuteScalarAsync();

        if (stored is long messageId && messageId > 0)
            try
            {
                return await channel.GetMessageAsync((ulong)messageId);
            }
            catch (Exception)
            {
                // deleted or unreachable, fall through to the guess
            }

        try
        {
            var messages = await channel.GetMessagesAsync();
            return messages.LastOrDefault();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Records that a human wrote in the ticket. Basis for the inactivity auto-close.</summary>
    public static async Task TouchActivityAsync(ulong channelId)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = con.CreateCommand(
            "UPDATE ticketcache SET last_activity = @now, reminder_sent_at = 0 WHERE tchannel_id = @channel");
        cmd.Parameters.AddWithValue("now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        cmd.Parameters.AddWithValue("channel", (long)channelId);
        await cmd.ExecuteNonQueryAsync();
    }

    private static string GenerateAdditionalNotes()
    {
        List<string> notes = [];

        var currentHour = DateTime.Now.Hour;
        if (currentHour >= 22 || currentHour <= 8) notes.Add("Aufgrund der Uhrzeit kann es zu Verzögerungen kommen.");

        if (DateTime.Now.Month == 12) notes.Add("Aufgrund der Weihnachtszeit kann es zu Verzögerungen kommen.");

        var additionalNotes = string.Join("\n", notes);

        if (!string.IsNullOrEmpty(additionalNotes))
            additionalNotes = $"\nNOTE: {additionalNotes} Danke für deine Geduld.";

        return additionalNotes;
    }

    public static async Task SendStaffNotice(CommandContext ctx, DiscordChannel ticket_channel, DiscordMember user)
    {
        var eb = new DiscordEmbedBuilder()
            .WithAuthor(ctx.User.GetFormattedUserName(), ctx.User.AvatarUrl)
            .WithColor(DiscordColor.Blurple).WithFooter("AGC-Support-System")
            .WithDescription(
                $"Hey {user.Mention}. Ein Ticket wurde von {ctx.User.Mention} mit dir erstellt. Bitte warte ab, bis sich das Teammitglied bei dir meldet.");
        await ticket_channel.SendMessageAsync(eb);
    }

    public static async Task SendUserNotice(DiscordChannel ticketChannel, DiscordUser user, TicketCategory category)
    {
        var text = string.IsNullOrWhiteSpace(category.WelcomeText)
            ? "Hey! Danke fürs öffnen eines Tickets. Ein Teammitglied wird sich gleich um dein Anliegen kümmern. Bitte teile uns in der Zeit alle nötigen Infos mit. "
            : category.WelcomeText;

        var eb = new DiscordEmbedBuilder()
            .WithAuthor(user.GetFormattedUserName(), user.AvatarUrl)
            .WithColor(DiscordColor.Blurple).WithFooter("AGC-Support-System")
            .WithDescription(text + GenerateAdditionalNotes());
        await ticketChannel.SendMessageAsync(eb);
    }

    public static async Task DeleteTicket(ComponentInteractionCreateEventArgs interaction)
    {
        var teamler = await TicketAccess.MayHandleAsync(await interaction.User.ConvertToMember(interaction.Guild),
            interaction.Channel);
        if (!teamler)
        {
            await interaction.Interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().WithContent("Du bist kein Teammitglied!").AsEphemeral());
            return;
        }

        await interaction.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);

        var del_ticketbutton =
            new DiscordButtonComponent(ButtonStyle.Danger, "ticket_delete", "Ticket löschen ❌", true);
        var imsg = await interaction.Channel.GetMessageAsync(interaction.Message.Id);
        var imsgmb = new DiscordMessageBuilder();
        imsgmb.WithContent(imsg.Content);
        imsgmb.AddEmbed(imsg.Embeds[0]);
        imsgmb.AddComponents(del_ticketbutton);
        await imsg.ModifyAsync(imsgmb);
        await NotificationManager.ClearMode(interaction.Channel.Id);
        var ebct = new DiscordEmbedBuilder()
            .WithTitle("Ticket wird gelöscht")
            .WithDescription(
                $"Löschen eingeleitet von {interaction.User.Mention} {interaction.User.GetFormattedUserName()} ``{interaction.User.Id}`` \nTicket wird in __5__ Sekunden gelöscht.")
            .WithColor(BotConfig.GetEmbedColor())
            .WithFooter("AGC-Support-System").Build();
        var mb = new DiscordMessageBuilder();
        mb.AddEmbed(ebct);
        var ms = await interaction.Channel.SendMessageAsync("Transcript wird generiert...");
        string transcriptURL;
        try
        {
            transcriptURL = await GenerateTranscript(interaction.Channel);
        }
        catch (Exception e)
        {
            await ErrorReporting.SendErrorToDev(CurrentApplication.DiscordClient, interaction.User, e);
            await ms.ModifyAsync(
                "Transcript konnte nicht generiert werden. Das Ticket wurde nicht gelöscht, bitte versuche es erneut oder wende dich an den Botentwickler.");
            return;
        }

        await InsertTransscriptIntoDB(interaction.Channel, TranscriptType.Team, transcriptURL);
        await ms.ModifyAsync("Transcript wurde generiert....");

        var channel = interaction.Channel;
        await channel.SendMessageAsync(mb);
        await Task.Delay(TimeSpan.FromSeconds(5));
        await SendTranscriptToLog(channel, transcriptURL, interaction.Interaction);
        await channel.DeleteAsync("Ticket wurde gelöscht");
        await DeleteCache(channel);
    }

    public static async Task ClaimTicket(ComponentInteractionCreateEventArgs interaction)
    {
        var teamler = await TicketAccess.MayHandleAsync(await interaction.User.ConvertToMember(interaction.Guild),
            interaction.Channel);
        if (!teamler)
        {
            await interaction.Interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().WithContent("Du bist kein Teammitglied!").AsEphemeral());
            return;
        }

        await Claim_UpdateHeaderComponents(interaction);
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        var query =
            $"SELECT ticket_id FROM ticketcache where claimed = False AND tchannel_id = '{(long)interaction.Interaction.ChannelId}'";

        await using var cmd = con.CreateCommand(query);
        await using var reader = await cmd.ExecuteReaderAsync();
        var ticket_id = "";
        while (await reader.ReadAsync()) ticket_id = reader.GetString(0);

        await reader.CloseAsync();

        var claimembed = new DiscordEmbedBuilder
        {
            Title = "Ticket geclaimed",
            Description = $"Das Ticket wurde von {interaction.User.Mention} ``{interaction.User.Id}`` geclaimed!",
            Color = DiscordColor.Green
        };
        claimembed.WithFooter(
            $"{interaction.User.GetFormattedUserName()} wird sich um dein Anliegen kümmern | {ticket_id}");

        await using var cmd2 =
            con.CreateCommand($"UPDATE ticketcache SET claimed = True WHERE ticket_id = '{ticket_id}'");
        await cmd2.ExecuteNonQueryAsync();

        await using var cmd3 =
            con.CreateCommand(
                $"UPDATE ticketcache SET claimed_from = '{(long)interaction.User.Id}' WHERE tchannel_id = '{(long)interaction.Interaction.ChannelId}'");
        await cmd3.ExecuteNonQueryAsync();
        await interaction.Interaction.Channel.SendMessageAsync(new DiscordMessageBuilder().AddEmbed(claimembed));
    }

    public static async Task AddUserToTicket(CommandContext ctx, DiscordChannel ticket_channel, DiscordUser user,
        bool addedAfter = false)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        var query = $"SELECT ticket_id FROM ticketcache where tchannel_id = '{(long)ticket_channel.Id}'";
        await using var cmd = con.CreateCommand(query);
        await using var reader = await cmd.ExecuteReaderAsync();
        var ticket_id = "";
        while (reader.Read()) ticket_id = reader.GetString(0);

        await reader.CloseAsync();
        await using var cmd2 =
            con.CreateCommand(
                $"UPDATE ticketcache SET ticket_users = array_append(ticket_users, '{(long)user.Id}') WHERE ticket_id = '{ticket_id}'");
        await cmd2.ExecuteNonQueryAsync();
        var channel = ticket_channel;
        var member = await ctx.Guild.GetMemberAsync(user.Id);
        await channel.AddOverwriteAsync(member,
            Permissions.AccessChannels | Permissions.SendMessages | Permissions.AddReactions | Permissions.AttachFiles |
            Permissions.EmbedLinks);
        if (addedAfter)
        {
            var afteraddembed = new DiscordEmbedBuilder
            {
                Title = "User hinzugefügt",
                Description = $"Der User {user.Mention} ``{user.Id}`` wurde zum Ticket hinzugefügt!",
                Color = DiscordColor.Green
            };
            var mb = new DiscordMessageBuilder().WithContent(user.Mention + " wurde zum Ticket hinzugefügt.")
                .AddEmbed(afteraddembed);
            var userEmbed = new DiscordEmbedBuilder()
                .WithTitle("Du wurdest zu einem Ticket hinzugefügt!")
                .WithDescription($"Du wurdest von {ctx.User.Mention} zu einem Ticket hinzugefügt!")
                .WithColor(DiscordColor.Green).Build();
            DiscordMessageBuilder userDM = new();
            userDM.AddEmbed(userEmbed);
            DiscordLinkButtonComponent button = new($"https://discord.com/channels/{ctx.Guild.Id}/{channel.Id}",
                "Zum Ticket");
            userDM.AddComponents(button);
            try
            {
                await member.SendMessageAsync(userDM);
            }
            catch
            {
                // ignored
            }
        }
    }

    public static async Task AddUserToTicket(DiscordInteraction interaction, DiscordChannel ticket_channel,
        DiscordUser user, bool addedAfter = false)
    {
        var teamler = await TicketAccess.MayHandleAsync(await interaction.User.ConvertToMember(interaction.Guild),
            interaction.Channel);
        if (!teamler && addedAfter)
        {
            await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().WithContent("Du bist kein Teammitglied!").AsEphemeral());
            return;
        }

        if (addedAfter) await interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);

        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        var query = $"SELECT ticket_id FROM ticketcache where tchannel_id = '{(long)ticket_channel.Id}'";
        await using var cmd = con.CreateCommand(query);
        await using var reader = await cmd.ExecuteReaderAsync();
        var ticket_id = "";
        while (reader.Read()) ticket_id = reader.GetString(0);

        await reader.CloseAsync();
        await using var cmd2 =
            con.CreateCommand(
                $"UPDATE ticketcache SET ticket_users = array_append(ticket_users, '{(long)user.Id}') WHERE ticket_id = '{ticket_id}'");
        await cmd2.ExecuteNonQueryAsync();
        var channel = ticket_channel;
        var member = await interaction.Guild.GetMemberAsync(user.Id);
        await channel.AddOverwriteAsync(member,
            Permissions.AccessChannels | Permissions.SendMessages | Permissions.AddReactions | Permissions.AttachFiles |
            Permissions.EmbedLinks);
        if (addedAfter)
        {
            var afteraddembed = new DiscordEmbedBuilder
            {
                Title = "User hinzugefügt",
                Description = $"Der User {user.Mention} ``{user.Id}`` wurde zum Ticket hinzugefügt!",
                Color = DiscordColor.Green
            };
            var mb = new DiscordMessageBuilder().WithContent(user.Mention + " wurde zum Ticket hinzugefügt.")
                .AddEmbed(afteraddembed);
            await interaction.Channel.SendMessageAsync(mb);
            var userEmbed = new DiscordEmbedBuilder()
                .WithTitle("Du wurdest zu einem Ticket hinzugefügt!")
                .WithDescription($"Du wurdest von {interaction.User.Mention} zu einem Ticket hinzugefügt!")
                .WithColor(DiscordColor.Green).Build();
            DiscordLinkButtonComponent button = new($"https://discord.com/channels/{interaction.Guild.Id}/{channel.Id}",
                "Zum Ticket");
            var userDM = new DiscordMessageBuilder().AddEmbed(userEmbed).AddComponents(button);
            try
            {
                await user.SendMessageAsync(userDM);
            }
            catch
            {
                // ignored
            }
        }
    }

    public static async Task GenerateTranscriptAndFlag(DiscordInteraction interaction)
    {
        var teamler = await TicketAccess.MayHandleAsync(await interaction.User.ConvertToMember(interaction.Guild),
            interaction.Channel);
        if (!teamler)
        {
            await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().WithContent("Du bist kein Teammitglied!").AsEphemeral());
            return;
        }

        DiscordEmbedBuilder eb = new()
        {
            Title = "Transscript",
            Description = "Bitte wähle den User aus, bei dem du dieses Ticket anhängen möchtest (Auto-Flag)!",
            Color = DiscordColor.Blurple
        };

        var usersel = new DiscordUserSelectComponent("Wähle einen User", "transcript_user_selector", 1);

        var irb = new DiscordInteractionResponseBuilder().AddEmbed(eb).AddComponents(usersel).AsEphemeral();
        await interaction.CreateResponseAsync(InteractionResponseType.UpdateMessage, irb);
    }

    public static async Task TranscriptFlag_Callback(DiscordInteraction interaction, DiscordClient client)
    {
        if (!await EnsureTeamAsync(interaction)) return;

        var users = interaction.Data.Values[0];
        var user = await interaction.Guild.GetMemberAsync(ulong.Parse(users));
        var channel = interaction.Channel;

        var idstring = $"FlagModal-{GenerateTicketID(3)}";
        DiscordInteractionModalBuilder modal = new();
        modal.WithTitle("Weitere Notizen zum Flag");
        modal.CustomId = idstring;
        modal.AddLabelComponent(new("Notiz", component: new DiscordTextInputComponent(TextComponentStyle.Small, minLength: 1, maxLength: 200, placeholder: "Gebe hier deine Notiz ein.")));
        await interaction.CreateInteractionModalResponseAsync(modal);
        var interactivity = client.GetInteractivity();
        var result = await interactivity.WaitForModalAsync(idstring, TimeSpan.FromMinutes(5));
        if (result.TimedOut) return;

        var notes = (result.Result.Interaction.Data.ModalComponents.OfType<DiscordLabelComponent>().First().Component as DiscordTextInputComponent)?.Value;
        await result.Result.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);

        var ticket_id = await GetTicketIdFromChannel(channel);
        var ticket_owner = await GetTicketOwnerFromChannel(channel);
        await interaction.EditOriginalResponseAsync(
            new DiscordWebhookBuilder().WithContent("Transcript wird generiert..."));
        var transcriptURL = await GenerateTranscript(channel);
        await interaction.EditOriginalResponseAsync(
            new DiscordWebhookBuilder().WithContent("Transcript wird in die Datenbank eingetragen..."));
        var con_db = BotConfig.GetConfig()["DatabaseCfg"]["Database"];
        var con_host = BotConfig.GetConfig()["DatabaseCfg"]["Database_Host"];
        var con_pass = BotConfig.GetConfig()["DatabaseCfg"]["Database_Password"];
        var con_user = BotConfig.GetConfig()["DatabaseCfg"]["Database_User"];
        var currentappid = client.CurrentApplication.Id;
        var caseid = Guid.NewGuid().ToString("N")[..8];
        var constring = $"Host={con_host};Username={con_user};Password={con_pass};Database={con_db}";
        var current_unix_timestamp = DateTimeOffset.Now.ToUnixTimeSeconds();
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd =
            con.CreateCommand(
                "INSERT INTO flags (description, userid, punisherid, datum, caseid) VALUES (@description, @userid, @punisherid, @datum, @caseid)");
        cmd.Parameters.AddWithValue("@description",
            $"Angehängtes Transcript aus {ticket_id} (Von User: {ticket_owner} -> {transcriptURL}  |  Dazugehörige Notiz: {notes}");
        cmd.Parameters.AddWithValue("@userid", (long)user.Id);
        cmd.Parameters.AddWithValue("@punisherid", (long)interaction.User.Id);
        cmd.Parameters.AddWithValue("@datum", current_unix_timestamp);
        cmd.Parameters.AddWithValue("@caseid", caseid);
        await cmd.ExecuteNonQueryAsync();
        await interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().WithContent(
            $"Transcript wurde in die Datenbank eingetragen bei {user.GetFormattedUserName()} ``{user.Id}`` eingetragen!"));
    }

    public static async Task RenderSnippetSelector(DiscordInteraction interaction)
    {
        var snippets = await SnippetManagerHelper.GetAllSnippetsAsync();

        if (snippets.Count == 0)
        {
            await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().WithContent("Es sind keine Snippets vorhanden!").AsEphemeral());
            return;
        }

        var chunkedSnippets = new List<List<(string snipId, string snippedText)>>();
        for (var i = 0; i < snippets.Count; i += 25) chunkedSnippets.Add([.. snippets.Skip(i).Take(25)]);

        var irb = new DiscordInteractionResponseBuilder();
        irb.WithContent("Wähle einen Snippet aus.");

        foreach (var snippetChunk in chunkedSnippets)
        {
            var options = new List<DiscordStringSelectComponentOption>();

            foreach (var (snipId, snippedText) in snippetChunk)
                options.Add(new DiscordStringSelectComponentOption(snipId, snipId,
                    snippedText.Truncate(80)));

            var selector = new DiscordStringSelectComponent(
                $"Wähle einen Snippet {chunkedSnippets.IndexOf(snippetChunk) + 1}",
                options, maxOptions: 1, minOptions: 1,
                customId: $"snippet_selector_{chunkedSnippets.IndexOf(snippetChunk) + 1}");
            irb.AddComponents(selector).AsEphemeral();
        }

        await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, irb);
    }


    public static async Task GenerateTranscriptButton(DiscordInteraction interaction)
    {
        var teamler = await TicketAccess.MayHandleAsync(await interaction.User.ConvertToMember(interaction.Guild),
            interaction.Channel);
        if (!teamler)
        {
            await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().WithContent("Du bist kein Teammitglied!").AsEphemeral());
            return;
        }

        var channel = interaction.Channel;
        var ticket_id = await GetTicketIdFromChannel(channel);
        var ticket_owner = await GetTicketOwnerFromChannel(channel);
        await interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);
        await interaction.EditOriginalResponseAsync(
            new DiscordWebhookBuilder().WithContent("Transcript wird generiert..."));
        var transcriptURL = await GenerateTranscript(channel);
        await interaction.EditOriginalResponseAsync(
            new DiscordWebhookBuilder().WithContent($"Transcript: {transcriptURL}"));
    }

    public static async Task UserInfo(DiscordInteraction interaction)
    {
        var users = await GetTicketUsers(interaction);
        var options = new List<DiscordStringSelectComponentOption>();
        foreach (var user in users)
            options.Add(new DiscordStringSelectComponentOption(user.GetFormattedUserName() + " ( " + user.Id + " )",
                user.Id.ToString()));

        var selector = new DiscordStringSelectComponent("Wähle einen User", options, maxOptions: 1,
            minOptions: 1, customId: "userinfo_selector");
        var irb = new DiscordInteractionResponseBuilder()
            .WithContent("Wähle ein User aus dessen infos du sehen willst.").AddComponents(selector).AsEphemeral();
        await interaction.CreateResponseAsync(InteractionResponseType.UpdateMessage, irb);
    }

    public static async Task UserInfo_Callback(ComponentInteractionCreateEventArgs args)
    {
        if (!await EnsureTeamAsync(args.Interaction)) return;

        var user = args.Interaction.Data.Values[0];
        var member = await args.Guild.GetMemberAsync(ulong.Parse(user));
        var joined_at = member.JoinedAt.Timestamp();
        var created_at = member.CreationTimestamp.Timestamp();
        var toprole_color = member.Color;
        var toprole = member.Roles?.FirstOrDefault();
        var rolemention = toprole?.Mention ?? "Keine Rolle";
        var prev_tickets = await GetTicketCountFromThisUser((long)member.Id) - 1;
        var voicestate = member.VoiceState;
        var eb = new DiscordEmbedBuilder()
            .WithTitle("Userinfo")
            .WithDescription($"Userinfo für {member.Mention} ``{member.Id}``")
            .WithColor(toprole_color)
            .AddField(new DiscordEmbedField("Beigetreten am", joined_at)
            ).AddField(new DiscordEmbedField("Erstellt am", created_at)
            ).AddField(new DiscordEmbedField("Aktueller Voice-Channel",
                voicestate != null ? voicestate.Channel.Mention : "Kein Voice-Channel")
            ).AddField(new DiscordEmbedField("Höchste Rolle", toprole != null ? toprole.Mention : "Keine Rolle")
            ).AddField(new DiscordEmbedField("Ticketcount", prev_tickets.ToString())).WithFooter("AGC-Support-System")
            .WithThumbnail(member.AvatarUrl)
            .WithImageUrl(member.BannerUrl);
        var irb = new DiscordInteractionResponseBuilder().AddEmbed(eb).AsEphemeral();
        await args.Interaction.CreateResponseAsync(InteractionResponseType.UpdateMessage, irb);
    }

    public static async Task<List<ulong>> GetTicketUserIdsAsync(DiscordChannel ticketChannel)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd =
            con.CreateCommand("SELECT ticket_users FROM ticketcache WHERE tchannel_id = @channel LIMIT 1");
        cmd.Parameters.AddWithValue("channel", (long)ticketChannel.Id);
        var result = await cmd.ExecuteScalarAsync();
        if (result is not long[] ids) return [];

        return [.. ids.Select(id => (ulong)id)];
    }

    /// <summary>
    ///     Drops a user from the ticket roster and takes their channel access away. The member may be null
    ///     when they already left the guild, in which case only the roster is cleaned up.
    /// </summary>
    public static async Task RemoveUserFromTicketAsync(DiscordChannel ticketChannel, ulong userId,
        DiscordMember? member)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using (var cmd = con.CreateCommand(
                         "UPDATE ticketcache SET ticket_users = array_remove(ticket_users, @user) " +
                         "WHERE tchannel_id = @channel"))
        {
            cmd.Parameters.AddWithValue("user", (long)userId);
            cmd.Parameters.AddWithValue("channel", (long)ticketChannel.Id);
            await cmd.ExecuteNonQueryAsync();
        }

        if (member is null) return;

        try
        {
            await ticketChannel.AddOverwriteAsync(member);
        }
        catch (Exception e)
        {
            CurrentApplication.Logger.Error(e, "Error while removing user from ticket");
        }
    }

    public static async Task<List<DiscordUser>> GetTicketUsers(DiscordInteraction interaction)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        var query = $"SELECT ticket_users FROM ticketcache where tchannel_id = '{(long)interaction.Channel.Id}'";
        await using var cmd = con.CreateCommand(query);
        await using var reader = await cmd.ExecuteReaderAsync();
        List<long> ticket_users = [];
        while (reader.Read())
        {
            var ticketUsersArray = (long[])reader.GetValue(0);
            ticket_users = [.. ticketUsersArray];
        }

        await reader.CloseAsync();
        List<DiscordUser> ticket_users_discord = [];
        foreach (var user in ticket_users)
        {
            var u = await interaction.Guild.GetMemberAsync((ulong)user);
            ticket_users_discord.Add(u);
        }

        return ticket_users_discord;
    }

    public static async Task<List<DiscordUser>> GetTicketUsers(DiscordChannel tchannel, DiscordClient client)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        var query = $"SELECT ticket_users FROM ticketcache where tchannel_id = '{(long)tchannel.Id}'";
        await using var cmd = con.CreateCommand(query);
        await using var reader = await cmd.ExecuteReaderAsync();
        List<long> ticket_users = [];
        while (reader.Read())
        {
            var ticketUsersArray = (long[])reader.GetValue(0);
            ticket_users = [.. ticketUsersArray];
        }

        await reader.CloseAsync();

        List<DiscordUser> ticket_users_discord = [];
        foreach (var user in ticket_users)
        {
            var u = await client.GetUserAsync((ulong)user);
            ticket_users_discord.Add(u);
        }

        return ticket_users_discord;
    }

    public static async Task<List<DiscordUser>> GetTicketUsers(DiscordChannel tchannel)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        var query = $"SELECT ticket_users FROM ticketcache where tchannel_id = '{(long)tchannel.Id}'";
        await using var cmd = con.CreateCommand(query);
        await using var reader = await cmd.ExecuteReaderAsync();
        List<long> ticket_users = [];
        while (reader.Read())
        {
            var ticketUsersArray = (long[])reader.GetValue(0);
            ticket_users = [.. ticketUsersArray];
        }

        await reader.CloseAsync();

        List<DiscordUser> ticket_users_discord = [];
        foreach (var user in ticket_users)
        {
            var u = await tchannel.Guild.GetMemberAsync((ulong)user);
            ticket_users_discord.Add(u);
        }

        return ticket_users_discord;
    }

    public static async Task RemoveUserFromTicket(DiscordInteraction interaction, DiscordChannel ticket_channel,
        DiscordUser user, bool noautomatic = false)
    {
        var teamler = await TicketAccess.MayHandleAsync(await interaction.User.ConvertToMember(interaction.Guild),
            interaction.Channel);
        if (!teamler)
        {
            await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().WithContent("Du bist kein Teammitglied!").AsEphemeral());
            return;
        }

        if (noautomatic) await interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);

        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        var query = $"SELECT ticket_id FROM ticketcache where tchannel_id = '{(long)ticket_channel.Id}'";
        await using var cmd = con.CreateCommand(query);
        await using var reader = await cmd.ExecuteReaderAsync();
        var ticket_id = "";
        while (reader.Read()) ticket_id = reader.GetString(0);

        await reader.CloseAsync();
        await using var cmd2 =
            con.CreateCommand(
                $"UPDATE ticketcache SET ticket_users = array_remove(ticket_users, '{(long)user.Id}') WHERE ticket_id = '{ticket_id}'");
        await cmd2.ExecuteNonQueryAsync();
        var channel = ticket_channel;
        var member = await interaction.Guild.GetMemberAsync(user.Id);
        try
        {
            await channel.AddOverwriteAsync(member);
        }
        catch (Exception e)
        {
            CurrentApplication.Logger.Error(e, "Error while removing user from ticket");
        }


        if (noautomatic)
        {
            var afteraddembed = new DiscordEmbedBuilder
            {
                Title = "User entfernt",
                Description = $"Der User {user.Mention} ``{member.Id}`` wurde vom Ticket entfernt!",
                Color = DiscordColor.Red
            };
            await interaction.Channel.SendMessageAsync(new DiscordMessageBuilder().AddEmbed(afteraddembed));
            var transcriptURL = "";
            try
            {
                transcriptURL = await GenerateTranscript(ticket_channel);
            }
            catch (Exception e)
            {
                await ErrorReporting.SendErrorToDev(CurrentApplication.DiscordClient, member, e);
            }

            await SendTranscriptsToUser(member, transcriptURL, RemoveType.Removed,
                ticket_channel.Name);
        }
    }

    public static async Task CloseTicketOnLastUserLeave(DiscordUser user, DiscordClient client)
    {
        var ticket_ids = await GetOpenTicketsFromUser(user);
        foreach (var ticket_id in ticket_ids)
        {
            var tchannel_id = await GetTicketChannelFromTicketID(ticket_id);
            var tchannel = await client.GetChannelAsync((ulong)tchannel_id);
            var ticket_users = await GetTicketUsers(tchannel, client);
            if (ticket_users.Count == 1) await TicketManager.CloseTicket(tchannel, client);
        }
    }

    private static async Task<List<string>> GetOpenTicketsFromUser(DiscordUser user)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        var query = $"SELECT ticket_id FROM ticketcache WHERE ticket_users @> ARRAY[{(long)user.Id}::bigint]";
        await using var cmd = con.CreateCommand(query);
        await using var reader = await cmd.ExecuteReaderAsync();
        List<string> ticket_ids = [];
        while (reader.Read()) ticket_ids.Add(reader.GetString(0));

        await reader.CloseAsync();
        return ticket_ids;
    }

    private static async Task<long> GetTicketChannelFromTicketID(string ticket_id)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        var query = $"SELECT tchannel_id FROM ticketcache where ticket_id = '{ticket_id}'";
        await using var cmd = con.CreateCommand(query);
        await using var reader = await cmd.ExecuteReaderAsync();
        long tchannel_id = 0;
        while (reader.Read()) tchannel_id = reader.GetInt64(0);

        await reader.CloseAsync();
        return tchannel_id;
    }

    public static async Task<bool> CheckIfUserIsInTicket(DiscordInteraction interaction, DiscordChannel ticket_channel,
        DiscordUser user)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        var query = $"SELECT ticket_users FROM ticketcache where tchannel_id = '{(long)ticket_channel.Id}'";
        await using var cmd = con.CreateCommand(query);
        await using var reader = await cmd.ExecuteReaderAsync();
        List<long> ticket_users = [];
        while (reader.Read())
        {
            var ticketUsersArray = (long[])reader.GetValue(0);
            ticket_users = [.. ticketUsersArray];
        }

        await reader.CloseAsync();
        if (ticket_users.Contains((long)user.Id)) return true;

        return false;
    }

    /// <summary>
    ///     Both user selectors in one ephemeral message. The two selectors and their callbacks are the same
    ///     ones the separate "hinzufügen" and "entfernen" buttons used, so nothing downstream changed.
    /// </summary>
    public static async Task ManageUsersSelector(DiscordInteraction interaction)
    {
        if (!await EnsureTeamAsync(interaction)) return;

        DiscordEmbedBuilder eb = new()
        {
            Title = "User verwalten",
            Description = "Oben jemanden auswählen, um ihn zum Ticket hinzuzufügen. Unten, um ihn zu entfernen.",
            Color = DiscordColor.Blurple
        };

        var irb = new DiscordInteractionResponseBuilder()
            .AddEmbed(eb)
            .AddComponents(new DiscordUserSelectComponent("Zum Ticket hinzufügen", "adduser_selector", 1))
            .AddComponents(new DiscordUserSelectComponent("Vom Ticket entfernen", "removeuser_selector", 1))
            .AsEphemeral();

        await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, irb);
    }

    public static async Task AddUserToTicketSelector(DiscordInteraction interaction)
    {
        var teamler = await TicketAccess.MayHandleAsync(await interaction.User.ConvertToMember(interaction.Guild),
            interaction.Channel);
        if (!teamler)
        {
            await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().WithContent("Du bist kein Teammitglied!").AsEphemeral());
            return;
        }

        DiscordEmbedBuilder eb = new()
        {
            Title = "User hinzufügen",
            Description = "Bitte wähle den User aus, den du hinzufügen möchtest!",
            Color = DiscordColor.Blurple
        };
        var uoptions = new DiscordUserSelectComponent[]
        {
            new("Wähle den User aus den du zum Ticket hinzufügen willst.", "adduser_selector", 1)
        };
        var irb = new DiscordInteractionResponseBuilder().AddEmbed(eb)
            .AddComponents(uoptions).AsEphemeral();
        await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, irb);
    }

    public static async Task RemoveUserFromTicketSelector(DiscordInteraction interaction)
    {
        var teamler = await TicketAccess.MayHandleAsync(await interaction.User.ConvertToMember(interaction.Guild),
            interaction.Channel);
        if (!teamler)
        {
            await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().WithContent("Du bist kein Teammitglied!").AsEphemeral());
            return;
        }

        DiscordEmbedBuilder eb = new()
        {
            Title = "User entfernen",
            Description = "Bitte wähle den User aus, den du entfernen möchtest!",
            Color = DiscordColor.Blurple
        };
        var uoptions = new DiscordUserSelectComponent[]
        {
            new("Wähle den User aus den du vom Ticket entfernen willst.", "removeuser_selector", 1)
        };
        var irb = new DiscordInteractionResponseBuilder().AddEmbed(eb)
            .AddComponents(uoptions).AsEphemeral();
        await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, irb);
    }

    public static async Task AddUserToTicketSelector_Callback(ComponentInteractionCreateEventArgs interaction)
    {
        if (!await EnsureTeamAsync(interaction.Interaction)) return;

        var values = interaction.Interaction.Data.Values;
        var user = values[0];
        var member = await interaction.Guild.GetMemberAsync(ulong.Parse(user));
        var ticket_channel = interaction.Channel;
        if (await CheckIfUserIsInTicket(interaction.Interaction, ticket_channel, member))
        {
            var alreadyinembed = new DiscordEmbedBuilder
            {
                Title = "User bereits im Ticket",
                Description = $"Der User {member.Mention} ``{member.Id}`` ist bereits im Ticket!",
                Color = DiscordColor.Red
            };
            await interaction.Interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().AddEmbed(alreadyinembed).AsEphemeral());
            return;
        }

        await AddUserToTicket(interaction.Interaction, ticket_channel, member, true);
    }

    public static async Task RemoveUserFromTicketSelector_Callback(ComponentInteractionCreateEventArgs interaction)
    {
        if (!await EnsureTeamAsync(interaction.Interaction)) return;

        var values = interaction.Interaction.Data.Values;
        var user = values[0];
        var member = await interaction.Guild.GetMemberAsync(ulong.Parse(user));
        var ticket_channel = interaction.Channel;
        if (!await CheckIfUserIsInTicket(interaction.Interaction, ticket_channel, member))
        {
            var alreadyinembed = new DiscordEmbedBuilder
            {
                Title = "User nicht im Ticket",
                Description = $"Der User {member.Mention} ``{member.Id}`` ist nicht im Ticket!",
                Color = DiscordColor.Red
            };
            await interaction.Interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().AddEmbed(alreadyinembed).AsEphemeral());
            return;
        }

        await RemoveUserFromTicket(interaction.Interaction, ticket_channel, member, true);
    }

    public static async Task SendTranscriptsToUser(DiscordMember member, string TransscriptURL, RemoveType removeType,
        string ticket_name)
    {
        if (removeType == RemoveType.Closed)
        {
            var eb = new DiscordEmbedBuilder().WithTitle("Transscript")
                .WithDescription($"Ticket ``{ticket_name}`` wurde geschlossen! \nTranscript: {TransscriptURL}")
                .WithColor(DiscordColor.Blurple);
            try
            {
                if (member.IsBot) return;
                await member.SendMessageAsync(eb);
            }
            catch (Exception)
            {
                await Task.CompletedTask;
            }
        }
        else if (removeType == RemoveType.Removed)
        {
            var eb = new DiscordEmbedBuilder().WithTitle("Transscript")
                .WithDescription($"Du wurdest aus Ticket ``{ticket_name}`` entfernt! \nTranscript: {TransscriptURL}")
                .WithColor(DiscordColor.Blurple);
            try
            {
                if (member.IsBot) return;
                await member.SendMessageAsync(eb);
            }
            catch (Exception)
            {
                await Task.CompletedTask;
            }
        }
    }

    public static async Task DeleteCache(DiscordChannel ticket_channel)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        var query = $"DELETE FROM ticketcache where tchannel_id = '{(long)ticket_channel.Id}'";
        await using var cmd = con.CreateCommand(query);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task<bool> IsTicket(DiscordChannel channel)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        var query = $"SELECT COUNT(*) FROM ticketcache where tchannel_id = '{(long)channel.Id}'";
        await using var cmd = con.CreateCommand(query);
        var rowCount = Convert.ToInt32(cmd.ExecuteScalar());
        if (rowCount > 0) return true;

        return false;
    }

    public static async Task<string> GenerateTranscript(DiscordChannel ticket_channel)
    {
        var BotToken = BotConfig.GetConfig()["MainConfig"]["Discord_API_Token"];
        var tick = await GetTicketIdFromChannel(ticket_channel);
        var id = GenerateTicketID(5);
        var outputFileName = $"{tick}-{id}.html";

        var baseDir = AppContext.BaseDirectory;
        var outputPath = Path.Combine(baseDir, "data", "tickets", "transcripts", outputFileName);
        var psi = new ProcessStartInfo
        {
            FileName = Path.Combine(baseDir, "tools", "exporter", "DiscordChatExporter.Cli"),
            Arguments =
                $"export -c {ticket_channel.Id} --media --reuse-media --media-dir data/tickets/transcripts/Assets -o data/tickets/transcripts/{outputFileName}",
            WorkingDirectory = baseDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.EnvironmentVariables["DISCORD_TOKEN"] = BotToken;
        var process = new Process { StartInfo = psi };
        process.Start();

        var stdErrTask = process.StandardError.ReadToEndAsync();
        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        var stdErr = await stdErrTask;
        await stdOutTask;

        if (process.ExitCode != 0 || !File.Exists(outputPath))
            throw new InvalidOperationException(
                $"Transcript-Export für Ticket {tick} ist fehlgeschlagen (Exit-Code {process.ExitCode}): {stdErr}");

        var baselink = $"https://ticketsystem.animegamingcafe.de/transcripts/{outputFileName}";

        _ = TicketSearchTools.LoadSingleTicketIntoCache(outputFileName);

        return baselink;
    }

    public static async Task InsertTransscriptIntoDB(DiscordChannel ticket_channel, TranscriptType transcriptType,
        string transcript_url)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        var query = $"SELECT ticket_id FROM ticketcache where tchannel_id = '{(long)ticket_channel.Id}'";
        await using var cmd = con.CreateCommand(query);
        await using var reader = await cmd.ExecuteReaderAsync();
        var ticket_id = "";
        while (reader.Read()) ticket_id = reader.GetString(0);

        await reader.CloseAsync();

        if (transcriptType == TranscriptType.User)
        {
            await using var cmd2 =
                con.CreateCommand(
                    $"UPDATE ticketstore SET user_transscript_url = '{transcript_url}' WHERE ticket_id = '{ticket_id}'");
            await cmd2.ExecuteNonQueryAsync();
        }
        else if (transcriptType == TranscriptType.Team)
        {
            await using var cmd2 =
                con.CreateCommand(
                    $"UPDATE ticketstore SET team_transscript_url = '{transcript_url}' WHERE ticket_id = '{ticket_id}'");
            await cmd2.ExecuteNonQueryAsync();
        }
    }


    public static async Task SendTranscriptToLog(DiscordChannel channel, string ticket_url,
        DiscordInteraction interaction)
    {
        DiscordEmbedBuilder eb = new();
        var ticket_owner = await GetTicketOwnerFromChannel(channel);
        var staff = interaction.User.Mention;

        eb.AddField(new DiscordEmbedField("Ticket Owner", $"<@{ticket_owner}>", true));
        eb.AddField(new DiscordEmbedField("Ticket Name", channel.Name, true));

        List<DiscordUser> users = [];
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        var query = $"SELECT ticket_users FROM ticketcache where tchannel_id = '{(long)channel.Id}'";
        await using var cmd = con.CreateCommand(query);
        await using var reader = await cmd.ExecuteReaderAsync();
        List<long> ticket_users = [];
        while (reader.Read())
        {
            var ticketUsersArray = (long[])reader.GetValue(0);
            ticket_users = [.. ticketUsersArray];
        }

        await reader.CloseAsync();
        var cusers = new StringBuilder();
        var messages = await channel.GetMessagesAsync();
        Dictionary<DiscordUser, int> userMessageCounts = [];

        foreach (var message in messages)
        {
            if (message.Author == messages[0].Author) continue;
            if (userMessageCounts.TryGetValue(message.Author, out int value))
                userMessageCounts[message.Author] = ++value;
            else
                userMessageCounts[message.Author] = 1;
        }

        foreach (var entry in userMessageCounts)
            cusers.AppendLine($"{entry.Key.Mention} ``{entry.Key.Id}`` - {entry.Value}");

        eb.AddField(new DiscordEmbedField("Nutzer im Ticket", cusers.Length > 0 ? cusers.ToString() : "Keine", true));


        eb.AddField(new DiscordEmbedField("Ticket URL", $"[Transcript Link]({ticket_url})", true));
        eb.AddField(new DiscordEmbedField("Ticket ID", await GetTicketIdFromChannel(channel), true));
        eb.AddField(new DiscordEmbedField("Staff", staff, true));
        eb.WithColor(DiscordColor.Blurple);
        eb.WithFooter($"User-ID = {ticket_owner}");
        eb.WithTimestamp(DateTime.Now);
        var logchannel = channel.Guild.GetChannel(ulong.Parse(BotConfig.GetConfig()["TicketConfig"]["LogChannelId"]));
        await logchannel.SendMessageAsync(eb);
    }

    public static async Task SendTranscriptToLog(CommandContext ctx, string ticket_url, DiscordClient client)
    {
        DiscordEmbedBuilder eb = new();
        var ticket_owner = await GetTicketOwnerFromChannel(ctx.Channel);

        eb.AddField(new DiscordEmbedField("Ticket Owner", $"<@{ticket_owner}>", true));
        eb.AddField(new DiscordEmbedField("Ticket Name", ctx.Channel.Name, true));

        List<DiscordUser> users = [];
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        var query = $"SELECT ticket_users FROM ticketcache where tchannel_id = '{(long)ctx.Channel.Id}'";
        await using var cmd = con.CreateCommand(query);
        await using var reader = await cmd.ExecuteReaderAsync();
        List<long> ticket_users = [];
        while (reader.Read())
        {
            var ticketUsersArray = (long[])reader.GetValue(0);
            ticket_users = [.. ticketUsersArray];
        }

        await reader.CloseAsync();
        var cusers = "";
        var messages = await ctx.Channel.GetMessagesAsync();
        HashSet<DiscordUser> userSet = [];
        foreach (var message in messages) userSet.Add(message.Author);

        foreach (var user in userSet) cusers += $"{user.Mention} ``{user.Id}``\n";

        eb.AddField(new DiscordEmbedField("Nutzer im Ticket", cusers, true));
        eb.AddField(new DiscordEmbedField("Ticket URL", $"[Transcript Link]({ticket_url})", true));
        eb.AddField(new DiscordEmbedField("Ticket ID", await GetTicketIdFromChannel(ctx.Channel), true));
        eb.AddField(new DiscordEmbedField("Staff", ctx.Message.Author.Mention, true));
        eb.WithColor(DiscordColor.Blurple);
        eb.WithFooter($"User-ID = {ticket_owner}");
        eb.WithTimestamp(DateTime.Now);
        var logchannel = ctx.Guild.GetChannel(ulong.Parse(BotConfig.GetConfig()["TicketConfig"]["LogChannelId"]));
        await logchannel.SendMessageAsync(eb);
    }
}

[EventHandler]
public class TicketManagerHelperListener : BaseCommandModule
{
    [Event]
    public static async Task GuildMemberRemoved(DiscordClient client, GuildMemberRemoveEventArgs args)
    {
        _ = Task.Run(async () => { await TicketManagerHelper.CloseTicketOnLastUserLeave(args.Member, client); });
    }
}