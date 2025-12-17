#region

using System.Diagnostics;
using System.Text;
using AGC_Management.Components;
using AGC_Management.Enums;
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

    public static async Task<int> GetPreviousTicketCount(TicketType ticketType)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        var query = $"SELECT COUNT(*) FROM ticketstore where tickettype = '{ticketType.ToString().ToLower()}'";
        await using var cmd = con.CreateCommand(query);
        var rowCount = Convert.ToInt32(cmd.ExecuteScalar());
        return rowCount;
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
        return new string(Enumerable.Repeat(chars, length)
            .Select(s => s[random.Next(s.Length)]).ToArray());
    }

    public static async Task<bool> CheckForOpenTicket(long user_id)
    {
        var isTicketOpen = false;
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        var query = $"SELECT COUNT(*) FROM ticketstore where ticket_owner = '{user_id}' AND closed = False";
        await using var cmd = con.CreateCommand(query);
        var rowCount = Convert.ToInt32(cmd.ExecuteScalar());
        if (rowCount > 0) isTicketOpen = true;

        return isTicketOpen;
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

    public static async Task<long> GetOpenTicketChannel(long user_id)
    {
        long channel_id = 0;
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        var query = $"SELECT tchannel_id FROM ticketcache where ticket_owner = '{user_id}'";
        await using var cmd = con.CreateCommand(query);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (reader.Read()) channel_id = reader.GetInt64(0);

        await reader.CloseAsync();
        return channel_id;
    }

    public static async Task Claim_UpdateHeaderComponents(ComponentInteractionCreateEventArgs interaction)
    {
        await interaction.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);
        var message = await interaction.Channel.GetMessageAsync(interaction.Message.Id);
        var mb = new DiscordMessageBuilder();
        mb.WithContent(message.Content);
        mb.AddEmbed(message.Embeds[0]);
        var components = TicketComponents.GetTicketClaimedActionRow();
        List<DiscordActionRowComponent> row = new()
        {
            new DiscordActionRowComponent(components)
        };
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

    public static async Task InsertHeaderIntoTicket(CommandContext ctx, DiscordChannel tchannel, DiscordMember member)
    {
        var pingstring = $"{member.Mention} | {ctx.User.Mention}";
        var ticket_channel = tchannel;
        var prev_tickets = await GetTicketCountFromThisUser((long)ctx.User.Id);
        var eb = new DiscordEmbedBuilder()
            .WithAuthor(member.UsernameWithDiscriminator, ctx.User.AvatarUrl)
            .WithColor(DiscordColor.Blurple)
            .WithFooter(
                $"Nutzer-ID: {member.Id} • Ticket-ID: {await GetTicketIdFromChannel(tchannel)}")
            .WithDescription("**Ticket-Typ: Support-Ticket**");
        var mb = new DiscordMessageBuilder();
        mb.WithContent(pingstring);
        mb.AddEmbed(eb.Build());
        var rowComponents = TicketComponents.GetTicketActionRow();
        List<DiscordActionRowComponent> row = new()
        {
            new DiscordActionRowComponent(rowComponents)
        };

        mb.AddComponents(row);
        await ticket_channel.SendMessageAsync(mb);
    }

    public static async Task InsertHeaderIntoTicket(DiscordInteraction interaction, DiscordChannel tchannel,
        TicketCreator ticketCreator, TicketType ticketType)
    {
        var pingstring = $"{interaction.User.Mention} | <@&{BotConfig.GetConfig()["TicketConfig"]["TeamRoleId"]}>";
        if (ticketType == TicketType.Report)
        {
            if (ticketCreator == TicketCreator.User)
            {
                var ticket_channel = tchannel;
                var prev_tickets = await GetTicketCountFromThisUser((long)interaction.User.Id);
                var eb = new DiscordEmbedBuilder()
                    .WithAuthor(interaction.User.UsernameWithDiscriminator, interaction.User.AvatarUrl)
                    .WithColor(DiscordColor.Blurple)
                    .WithFooter(
                        $"Nutzer-ID: {interaction.User.Id} • Ticket-ID: {await GetTicketIdFromChannel(tchannel)}")
                    .WithDescription("**Ticket-Typ: Report-Ticket**");
                var mb = new DiscordMessageBuilder();
                mb.WithContent(pingstring);
                mb.AddEmbed(eb.Build());
                var rowComponents = TicketComponents.GetTicketActionRow();
                List<DiscordActionRowComponent> row = new()
                {
                    new DiscordActionRowComponent(rowComponents)
                };

                mb.AddComponents(row);
                await ticket_channel.SendMessageAsync(mb);
            }
        }
        else if (ticketType == TicketType.Support)
        {
            if (ticketCreator == TicketCreator.User)
            {
                var ticket_channel = tchannel;
                var prev_tickets = await GetTicketCountFromThisUser((long)interaction.User.Id);

                var eb = new DiscordEmbedBuilder()
                    .WithAuthor(interaction.User.UsernameWithDiscriminator, interaction.User.AvatarUrl)
                    .WithColor(DiscordColor.Blurple)
                    .WithFooter(
                        $"Nutzer-ID: {interaction.User.Id} • Ticket-ID: {await GetTicketIdFromChannel(tchannel)}")
                    .WithDescription("**Ticket-Typ: Support-Ticket**");
                var mb = new DiscordMessageBuilder();
                mb.WithContent(pingstring);
                mb.AddEmbed(eb.Build());
                var rowComponents = TicketComponents.GetTicketActionRow();
                List<DiscordActionRowComponent> row = new()
                {
                    new DiscordActionRowComponent(rowComponents)
                };

                mb.AddComponents(row);
                await ticket_channel.SendMessageAsync(mb);
            }
        }
    }

    private static string GenerateAdditionalNotes()
    {
        List<string> notes = new();

        // Check if it's between 10 pm and 8 am
        var currentHour = DateTime.Now.Hour;
        if (currentHour >= 22 || currentHour <= 8) notes.Add("Aufgrund der Uhrzeit kann es zu Verzögerungen kommen.");

        // Check if it's Christmas
        if (DateTime.Now.Month == 12) notes.Add("Aufgrund der Weihnachtszeit kann es zu Verzögerungen kommen.");

        // Combine the notes
        var additionalNotes = string.Join("\n", notes);

        // Add a general note
        if (!string.IsNullOrEmpty(additionalNotes))
            additionalNotes = $"\nNOTE: {additionalNotes} Danke für deine Geduld.";

        return additionalNotes;
    }

    public static async Task SendStaffNotice(CommandContext ctx, DiscordChannel ticket_channel, DiscordMember user)
    {
        var eb = new DiscordEmbedBuilder()
            .WithAuthor(ctx.User.UsernameWithDiscriminator, ctx.User.AvatarUrl)
            .WithColor(DiscordColor.Blurple).WithFooter("AGC-Support-System")
            .WithDescription(
                $"Hey {user.Mention}. Ein Ticket wurde von {ctx.User.Mention} mit dir erstellt. Bitte warte ab, bis sich das Teammitglied bei dir meldet.");
        await ticket_channel.SendMessageAsync(eb);
    }

    public static async Task SendUserNotice(DiscordInteraction interaction, DiscordChannel ticket_channel,
        TicketType ticketType)
    {
        if (ticketType == TicketType.Report)
        {
            var eb = new DiscordEmbedBuilder()
                .WithAuthor(interaction.User.UsernameWithDiscriminator, interaction.User.AvatarUrl)
                .WithColor(DiscordColor.Blurple).WithFooter("AGC-Support-System")
                .WithDescription(
                    $"Hey! Danke fürs öffnen eines Report-Tickets. Ein Teammitglied wird sich gleich um dein Anliegen kümmern. Bitte teile uns in der Zeit alle nötigen Infos mit.\n" +
                    $"1. Um wen geht es (User-ID oder User-Name)\n" +
                    $"2. Was ist vorgefallen (Bitte versuche die Situation so ausführlich wie möglich zu beschreiben)\n " +
                    $"3. Hast du eventuelle Beweise? {GenerateAdditionalNotes()}");
            await ticket_channel.SendMessageAsync(eb);
        }
        else if (ticketType == TicketType.Support)
        {
            var eb = new DiscordEmbedBuilder()
                .WithAuthor(interaction.User.UsernameWithDiscriminator, interaction.User.AvatarUrl)
                .WithColor(DiscordColor.Blurple).WithFooter("AGC-Support-System")
                .WithDescription(
                    $"Hey! Danke fürs öffnen eines Support-Tickets. Ein Teammitglied wird sich gleich um dein Anliegen kümmern. Bitte teile uns in der Zeit alle nötigen Infos mit. {GenerateAdditionalNotes()}");
            await ticket_channel.SendMessageAsync(eb);
        }
    }

    public static async Task DeleteTicket(ComponentInteractionCreateEventArgs interaction)
    {
        var teamler = TeamChecker.IsSupporter(await interaction.User.ConvertToMember(interaction.Guild));
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
                $"Löschen eingeleitet von {interaction.User.Mention} {interaction.User.UsernameWithDiscriminator} ``{interaction.User.Id}`` \nTicket wird in __5__ Sekunden gelöscht.")
            .WithColor(BotConfig.GetEmbedColor())
            .WithFooter("AGC-Support-System").Build();
        var mb = new DiscordMessageBuilder();
        mb.AddEmbed(ebct);
        var ms = await interaction.Channel.SendMessageAsync("Transcript wird generiert...");
        var transcriptURL = await GenerateTranscript(interaction.Channel);
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
        var teamler = TeamChecker.IsSupporter(await interaction.User.ConvertToMember(interaction.Guild));
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
            $"{interaction.User.UsernameWithDiscriminator} wird sich um dein Anliegen kümmern | {ticket_id}");

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
        // add perms
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
        var teamler = TeamChecker.IsSupporter(await interaction.User.ConvertToMember(interaction.Guild));
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
        // add perms
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
        // user selector
        var teamler = TeamChecker.IsSupporter(await interaction.User.ConvertToMember(interaction.Guild));
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
        var caseid = Guid.NewGuid().ToString("N").Substring(0, 8);
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
            $"Transcript wurde in die Datenbank eingetragen bei {user.UsernameWithDiscriminator} ``{user.Id}`` eingetragen!"));
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
        for (var i = 0; i < snippets.Count; i += 25) chunkedSnippets.Add(snippets.Skip(i).Take(25).ToList());

        var irb = new DiscordInteractionResponseBuilder();
        irb.WithContent("Wähle einen Snippet aus.");

        foreach (var snippetChunk in chunkedSnippets)
        {
            var options = new List<DiscordStringSelectComponentOption>();

            foreach (var snippet in snippetChunk)
                options.Add(new DiscordStringSelectComponentOption(snippet.snipId, snippet.snipId,
                    snippet.snippedText.Truncate(80)));

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
        var teamler = TeamChecker.IsSupporter(await interaction.User.ConvertToMember(interaction.Guild));
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
        // generate stringselector
        var options = new List<DiscordStringSelectComponentOption>();
        foreach (var user in users)
            options.Add(new DiscordStringSelectComponentOption(user.UsernameWithDiscriminator + " ( " + user.Id + " )",
                user.Id.ToString()));

        var selector = new DiscordStringSelectComponent("Wähle einen User", options, maxOptions: 1,
            minOptions: 1, customId: "userinfo_selector");
        var irb = new DiscordInteractionResponseBuilder()
            .WithContent("Wähle ein User aus dessen infos du sehen willst.").AddComponents(selector).AsEphemeral();
        // Update original
        await interaction.CreateResponseAsync(InteractionResponseType.UpdateMessage, irb);
    }

    public static async Task UserInfo_Callback(ComponentInteractionCreateEventArgs args)
    {
        // get user
        var user = args.Interaction.Data.Values[0];
        var member = await args.Guild.GetMemberAsync(ulong.Parse(user));
        // gather infos
        var joined_at = member.JoinedAt.Timestamp();
        var created_at = member.CreationTimestamp.Timestamp();
        var toprole_color = member.Color;
        var toprole = member.Roles?.FirstOrDefault();
        var rolemention = toprole?.Mention ?? "Keine Rolle";
        // get prev ticketcount
        var prev_tickets = await GetTicketCountFromThisUser((long)member.Id) - 1;
        var voicestate = member.VoiceState;
        // generate embed
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

    public static async Task<List<DiscordUser>> GetTicketUsers(DiscordInteraction interaction)
    {
        // get them to list
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        var query = $"SELECT ticket_users FROM ticketcache where tchannel_id = '{(long)interaction.Channel.Id}'";
        await using var cmd = con.CreateCommand(query);
        await using var reader = await cmd.ExecuteReaderAsync();
        List<long> ticket_users = new();
        while (reader.Read())
        {
            var ticketUsersArray = (long[])reader.GetValue(0);
            ticket_users = new List<long>(ticketUsersArray);
        }

        await reader.CloseAsync();
        List<DiscordUser> ticket_users_discord = new();
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
        List<long> ticket_users = new();
        while (reader.Read())
        {
            var ticketUsersArray = (long[])reader.GetValue(0);
            ticket_users = new List<long>(ticketUsersArray);
        }

        await reader.CloseAsync();

        List<DiscordUser> ticket_users_discord = new();
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
        List<long> ticket_users = new();
        while (reader.Read())
        {
            var ticketUsersArray = (long[])reader.GetValue(0);
            ticket_users = new List<long>(ticketUsersArray);
        }

        await reader.CloseAsync();

        List<DiscordUser> ticket_users_discord = new();
        foreach (var user in ticket_users)
        {
            var u = await tchannel.Guild.GetMemberAsync((ulong)user);
            ticket_users_discord.Add(u);
        }

        return ticket_users_discord;
    }

    public static async Task RemoveUserFromTicket(CommandContext ctx, DiscordChannel ticket_channel,
        DiscordUser user, bool noautomatic = false)
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
                $"UPDATE ticketcache SET ticket_users = array_remove(ticket_users, '{(long)user.Id}') WHERE ticket_id = '{ticket_id}'");
        await cmd2.ExecuteNonQueryAsync();
        var channel = ticket_channel;
        var member = await ctx.Guild.GetMemberAsync(user.Id);
        await channel.AddOverwriteAsync(member);
        if (noautomatic)
        {
            var afteraddembed = new DiscordEmbedBuilder
            {
                Title = "User entfernt",
                Description = $"Der User {user.Mention} ``{member.Id}`` wurde vom Ticket entfernt!",
                Color = DiscordColor.Red
            };
            await ctx.Channel.SendMessageAsync(new DiscordMessageBuilder().AddEmbed(afteraddembed));

            var tr = await GenerateTranscript(ctx.Channel);

            var userembed = new DiscordEmbedBuilder
            {
                Title = ticket_channel.Name,
                Description = $"Du wurdest aus dem Ticket ``{ticket_channel.Name}`` entfernt!",
                Color = DiscordColor.Green
            };
        }
    }


    public static async Task RemoveUserFromTicket(DiscordChannel ticket_channel,
        DiscordUser user, DiscordClient client, bool noautomatic = false)
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
                $"UPDATE ticketcache SET ticket_users = array_remove(ticket_users, '{(long)user.Id}') WHERE ticket_id = '{ticket_id}'");
        await cmd2.ExecuteNonQueryAsync();
        var channel = ticket_channel;
        var member = await client.GetUserAsync(user.Id);
        //await channel.AddOverwriteAsync(member);
        if (noautomatic)
        {
            var afteraddembed = new DiscordEmbedBuilder
            {
                Title = "User entfernt",
                Description = $"Der User {user.Mention} ``{member.Id}`` wurde vom Ticket entfernt!",
                Color = DiscordColor.Red
            };
            await ticket_channel.SendMessageAsync(new DiscordMessageBuilder().AddEmbed(afteraddembed));

            var tr = await GenerateTranscript(ticket_channel);

            var userembed = new DiscordEmbedBuilder
            {
                Title = ticket_channel.Name,
                Description = $"Du wurdest aus dem Ticket ``{ticket_channel.Name}`` entfernt!",
                Color = DiscordColor.Green
            };
        }
    }

    public static async Task RemoveUserFromTicket(DiscordInteraction interaction, DiscordChannel ticket_channel,
        DiscordUser user, bool noautomatic = false)
    {
        var teamler = TeamChecker.IsSupporter(await interaction.User.ConvertToMember(interaction.Guild));
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
            var tr = await GenerateTranscript(interaction.Channel);
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
        List<string> ticket_ids = new();
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
        List<long> ticket_users = new();
        while (reader.Read())
        {
            var ticketUsersArray = (long[])reader.GetValue(0);
            ticket_users = new List<long>(ticketUsersArray);
        }

        await reader.CloseAsync();
        if (ticket_users.Contains((long)user.Id)) return true;

        return false;
    }

    public static async Task AddUserToTicketSelector(DiscordInteraction interaction)
    {
        var teamler = TeamChecker.IsSupporter(await interaction.User.ConvertToMember(interaction.Guild));
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
        var teamler = TeamChecker.IsSupporter(await interaction.User.ConvertToMember(interaction.Guild));
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
        var psi = new ProcessStartInfo();
        var BotToken = BotConfig.GetConfig()["MainConfig"]["Discord_API_Token"];
        var tick = await GetTicketIdFromChannel(ticket_channel);
        var id = GenerateTicketID(5);
        psi.FileName = "tools/exporter/DiscordChatExporter.Cli";
        
        psi.Arguments =
            $"export -t \"{BotToken}\" -c {ticket_channel.Id} --media --reuse-media --media-dir data/tickets/transcripts/Assets -o data/tickets/transcripts/{tick}-{id}.html";
        psi.RedirectStandardOutput = true;
        var process = new Process();
        process.StartInfo = psi;
        process.Start();

        await process.WaitForExitAsync();
        var baselink = $"https://ticketsystem.animegamingcafe.de/transcripts/" + $"{tick}-{id}.html";

        _ = TicketSearchTools.LoadSingleTicketIntoCache($"{tick}-{id}.html");

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
            // insert
            await using var cmd2 =
                con.CreateCommand(
                    $"UPDATE ticketstore SET user_transscript_url = '{transcript_url}' WHERE ticket_id = '{ticket_id}'");
            await cmd2.ExecuteNonQueryAsync();
        }
        else if (transcriptType == TranscriptType.Team)
        {
            // insert
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

        List<DiscordUser> users = new();
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        var query = $"SELECT ticket_users FROM ticketcache where tchannel_id = '{(long)channel.Id}'";
        await using var cmd = con.CreateCommand(query);
        await using var reader = await cmd.ExecuteReaderAsync();
        List<long> ticket_users = new();
        while (reader.Read())
        {
            var ticketUsersArray = (long[])reader.GetValue(0);
            ticket_users = new List<long>(ticketUsersArray);
        }

        await reader.CloseAsync();
        var cusers = new StringBuilder();
        var messages = await channel.GetMessagesAsync();
        Dictionary<DiscordUser, int> userMessageCounts = new();

        foreach (var message in messages)
        {
            if (message.Author == messages[0].Author) continue;
            if (userMessageCounts.ContainsKey(message.Author))
                userMessageCounts[message.Author]++;
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

        List<DiscordUser> users = new();
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        var query = $"SELECT ticket_users FROM ticketcache where tchannel_id = '{(long)ctx.Channel.Id}'";
        await using var cmd = con.CreateCommand(query);
        await using var reader = await cmd.ExecuteReaderAsync();
        List<long> ticket_users = new();
        while (reader.Read())
        {
            var ticketUsersArray = (long[])reader.GetValue(0);
            ticket_users = new List<long>(ticketUsersArray);
        }

        await reader.CloseAsync();
        var cusers = "";
        var messages = await ctx.Channel.GetMessagesAsync();
        HashSet<DiscordUser> userSet = new();
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