#region

using AGC_Management.Components;
using AGC_Management.Entities.Ticket;
using AGC_Management.Enums;
using AGC_Management.Services;
using AGC_Management.Utils;

#endregion

namespace AGC_Management.Managers;

public class TicketManager
{
    private const Permissions TeamPermissions = Permissions.AccessChannels | Permissions.SendMessages |
                                                Permissions.ReadMessageHistory | Permissions.AttachFiles |
                                                Permissions.EmbedLinks | Permissions.ManageMessages;

    private static NpgsqlDataSource Db =>
        CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

    /// <summary>
    ///     Channel of an already open ticket that blocks a new one, or null when the user is within both
    ///     limits. The total limit is what the system enforced before categories existed; the per category
    ///     limit is the new one on top.
    /// </summary>
    public static async Task<ulong?> FindBlockingTicketAsync(ulong userId, TicketCategory category)
    {
        var totalLimit =
            await RuntimeSettings.GetIntAsync(TicketCategoryService.SettingsSection, "MaxOpenPerUserTotal", 1);
        if (totalLimit > 0 && await TicketManagerHelper.CountOpenTicketsAsync(userId) >= totalLimit)
            return (ulong)await TicketManagerHelper.GetOpenTicketChannel((long)userId);

        if (category.MaxOpenPerUser > 0 &&
            await TicketManagerHelper.CountOpenTicketsAsync(userId, category.CustomId) >= category.MaxOpenPerUser)
            return (ulong)await TicketManagerHelper.GetOpenTicketChannel((long)userId, category.CustomId);

        return null;
    }

    /// <summary>
    ///     Permission overwrites for a ticket channel of this category. It starts from what the parent
    ///     category grants, because that is exactly what a ticket channel inherits today, and only adds the
    ///     handler roles on top. Replacing the set outright would silently drop everyone who reaches
    ///     tickets through the category: admins, other bots, anyone the server owner allowed there.
    /// </summary>
    public static async Task<List<DiscordOverwriteBuilder>> BuildOverwritesAsync(DiscordGuild guild,
        TicketCategory category, DiscordChannel? parent)
    {
        var roleIds = category.HandlerRoleIds
            .Concat(await TicketAccess.GlobalAccessRoleIdsAsync())
            .Distinct()
            .ToList();

        List<DiscordOverwriteBuilder> team = [];
        foreach (var roleId in roleIds)
        {
            if (!guild.Roles.TryGetValue(roleId, out var role)) continue;
            team.Add(new DiscordOverwriteBuilder(role).Allow(TeamPermissions));
        }

        // Nothing to add: let the channel inherit from its category the way it always did.
        if (team.Count == 0) return [];

        if (parent is null)
        {
            // Without a parent the channel would be visible to everyone, so @everyone is denied here.
            team.Insert(0, new DiscordOverwriteBuilder(guild.EveryoneRole).Deny(Permissions.AccessChannels));
            return team;
        }

        var merged = parent.PermissionOverwrites.Select(overwrite => overwrite.ConvertToBuilder()).ToList();
        foreach (var builder in team)
        {
            var existing = merged.FirstOrDefault(entry => entry.Target == builder.Target);
            if (existing is null)
                merged.Add(builder);
            else
                existing.Allow(TeamPermissions).Remove(Permissions.None);
        }

        return merged;
    }

    public static DiscordChannel? ResolveParent(DiscordGuild guild, TicketCategory category)
    {
        var categoryId = category.DiscordCategoryId;
        if (categoryId == 0)
            try
            {
                categoryId = ulong.Parse(BotConfig.GetConfig()["TicketConfig"]["SupportCategoryId"]);
            }
            catch (Exception)
            {
                return null;
            }

        return guild.Channels.TryGetValue(categoryId, out var parent) ? parent : null;
    }

    /// <summary>
    ///     Writes the category's current role access onto the tickets that are already open. Without this a
    ///     role change in the dashboard would only reach tickets opened afterwards.
    /// </summary>
    public static async Task<int> ReapplyOverwritesAsync(TicketCategory category)
    {
        var guild = CurrentApplication.TargetGuild;
        if (guild is null) return 0;

        var roleOverwrites = await BuildOverwritesAsync(guild, category, ResolveParent(guild, category));
        if (roleOverwrites.Count == 0) return 0;

        List<ulong> channelIds = [];
        await using (var cmd = Db.CreateCommand(
                         "SELECT c.tchannel_id FROM ticketcache c JOIN ticketstore s ON s.ticket_id = c.ticket_id " +
                         "WHERE s.closed = false AND s.tickettype = @category"))
        {
            cmd.Parameters.AddWithValue("category", category.CustomId);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) channelIds.Add((ulong)reader.GetInt64(0));
        }

        var updated = 0;
        foreach (var channelId in channelIds)
        {
            if (!guild.Channels.TryGetValue(channelId, out var channel)) continue;

            var memberOverwrites = channel.PermissionOverwrites
                .Where(overwrite => overwrite.Type == OverwriteType.Member)
                .Select(overwrite => overwrite.ConvertToBuilder())
                .Where(builder => roleOverwrites.All(role => role.Target != builder.Target))
                .ToList();

            try
            {
                await channel.ModifyAsync(model =>
                {
                    model.PermissionOverwrites = [.. roleOverwrites, .. memberOverwrites];
                    model.AuditLogReason = $"Rechte der Kategorie {category.Label} neu angewendet";
                });
                updated++;
            }
            catch (Exception e)
            {
                CurrentApplication.Logger.Warning(e, "Could not reapply overwrites on ticket channel {Channel}",
                    channelId);
            }
        }

        return updated;
    }

    private static async Task<(string TicketId, DiscordChannel Channel)> CreateTicketAsync(DiscordGuild guild,
        TicketCategory category, DiscordUser owner, string auditReason)
    {
        var ticketId = TicketManagerHelper.GenerateTicketID();
        var number = await TicketCategoryService.NextTicketNumberAsync(category.CustomId);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        await using (var insert = Db.CreateCommand(
                         "INSERT INTO ticketstore (ticket_id, ticket_owner, tickettype, closed, opened_at) " +
                         "VALUES (@ticket, @owner, @category, false, @opened)"))
        {
            insert.Parameters.AddWithValue("ticket", ticketId);
            insert.Parameters.AddWithValue("owner", (long)owner.Id);
            insert.Parameters.AddWithValue("category", category.CustomId);
            insert.Parameters.AddWithValue("opened", now);
            await insert.ExecuteNonQueryAsync();
        }

        var parent = ResolveParent(guild, category);
        var channel = await guild.CreateChannelAsync($"{category.ChannelPrefix}-{number}", ChannelType.Text,
            parent, auditReason,
            overwrites: await BuildOverwritesAsync(guild, category, parent), reason: auditReason);

        await using (var cache = Db.CreateCommand(
                         "INSERT INTO ticketcache (ticket_id, ticket_owner, tchannel_id, claimed, ticket_users, last_activity) " +
                         "VALUES (@ticket, @owner, @channel, false, '{}', @now)"))
        {
            cache.Parameters.AddWithValue("ticket", ticketId);
            cache.Parameters.AddWithValue("owner", (long)owner.Id);
            cache.Parameters.AddWithValue("channel", (long)channel.Id);
            cache.Parameters.AddWithValue("now", now);
            await cache.ExecuteNonQueryAsync();
        }

        await TicketCategoryService.LogEventAsync(ticketId, "opened", owner.Id, category.CustomId);
        return (ticketId, channel);
    }

    /// <summary>Staff opening a ticket with a member, via !contact.</summary>
    public static async Task<DiscordChannel?> OpenTicket(CommandContext context, TicketCreator ticketCreator,
        DiscordMember discordMember)
    {
        var category = await TicketCategoryService.GetAsync("support")
                       ?? (await TicketCategoryService.GetAllAsync()).FirstOrDefault();
        if (category is null)
        {
            await context.RespondAsync(EmbedGenerator.GetErrorEmbed(
                "Es ist keine Ticket-Kategorie konfiguriert. Bitte lege im Dashboard eine an."));
            return null;
        }

        // No limit check here on purpose. The limit exists to stop a user from opening ticket after
        // ticket through the panel; a team member reaching out deliberately is not that.
        var (ticketId, channel) = await CreateTicketAsync(context.Guild, category, discordMember,
            $"Ticket erstellt von {context.User.GetFormattedUserName()} zu {discordMember.GetFormattedUserName()}");

        await Task.Delay(TimeSpan.FromSeconds(2));
        await TicketManagerHelper.AddUserToTicket(context, channel, discordMember);
        await TicketManagerHelper.InsertHeaderIntoTicket(channel, ticketId, category, discordMember,
            $"{discordMember.Mention} | {context.User.Mention}");
        await TicketManagerHelper.SendStaffNotice(context, channel, discordMember);
        return channel;
    }

    /// <summary>A user opening a ticket from the support panel.</summary>
    public static async Task OpenTicketAsync(DiscordInteraction interaction, TicketCategory category,
        IReadOnlyList<TicketIntakeAnswer>? answers = null, bool alreadyDeferred = false)
    {
        var guild = interaction.Guild;
        var user = interaction.User;

        var blocking = await FindBlockingTicketAsync(user.Id, category);
        if (blocking is not null)
        {
            var eb = new DiscordEmbedBuilder
            {
                Title = "Fehler | Bereits ein Ticket geöffnet!",
                Description = $"Du hast bereits ein geöffnetes Ticket! -> <#{blocking}>",
                Color = DiscordColor.Red
            };
            var link = new DiscordLinkButtonComponent(
                $"https://discord.com/channels/{guild.Id}/{blocking}", "Zum Ticket");

            if (alreadyDeferred)
                await interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
                    .AddEmbed(eb).AddComponents(link).AsEphemeral());
            else
                await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                    new DiscordInteractionResponseBuilder().AddEmbed(eb).AddComponents(link).AsEphemeral());
            return;
        }

        if (!alreadyDeferred)
        {
            var pending = new DiscordEmbedBuilder
            {
                Title = "Ticket erstellen",
                Description = "Du hast ein Ticket erstellt! Bitte warte einen Augenblick...",
                Color = DiscordColor.Blurple
            };
            await interaction.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().AddEmbed(pending).AsEphemeral());
        }

        var (ticketId, channel) = await CreateTicketAsync(guild, category, user,
            $"Ticket erstellt von {user.GetFormattedUserName()}");

        if (answers is { Count: > 0 }) await TicketCategoryService.SaveIntakeAnswersAsync(ticketId, answers);

        await Task.Delay(TimeSpan.FromSeconds(2));
        await TicketManagerHelper.AddUserToTicket(interaction, channel, user);

        var pings = category.EffectivePingRoleIds.Select(id => $"<@&{id}>");
        var pingContent = $"{user.Mention} | {string.Join(" ", pings)}".TrimEnd();
        await TicketManagerHelper.InsertHeaderIntoTicket(channel, ticketId, category, user, pingContent, answers);

        var created = new DiscordEmbedBuilder
        {
            Title = "Ticket erstellt",
            Description = $"Dein Ticket wurde erfolgreich erstellt! -> <#{channel.Id}>",
            Color = DiscordColor.Green
        };
        var jump = new DiscordLinkButtonComponent($"https://discord.com/channels/{guild.Id}/{channel.Id}",
            "Zum Ticket");
        await interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
            .AddEmbed(created).AddComponents(jump).AsEphemeral());

        await TicketManagerHelper.SendUserNotice(channel, user, category);
    }

    public static async Task CloseTicket(CommandContext ctx, DiscordChannel ticket_channel)
    {
        await CloseTicketAsync(ticket_channel, ctx.User);
    }

    public static async Task CloseTicket(DiscordChannel ticket_channel, DiscordClient client)
    {
        await CloseTicketAsync(ticket_channel, client.CurrentUser, "Letzter Ticketuser nicht mehr auf dem Server");
    }

    public static async Task CloseTicket(ComponentInteractionCreateEventArgs interaction, DiscordChannel ticket_channel)
    {
        var member = await interaction.Interaction.User.ConvertToMember(interaction.Interaction.Guild);
        if (!await TicketAccess.MayHandleAsync(member, ticket_channel))
        {
            await interaction.Interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().WithContent("Du bist kein Teammitglied!").AsEphemeral());
            return;
        }

        await interaction.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);
        await CloseTicketAsync(ticket_channel, interaction.User);
    }

    /// <summary>
    ///     Soft close: transcript, database, renamed channel, users removed and notified. The channel and
    ///     the cache row survive until someone presses "Ticket löschen".
    /// </summary>
    public static async Task CloseTicketAsync(DiscordChannel ticketChannel, DiscordUser closedBy,
        string? reason = null)
    {
        await NotificationManager.ClearMode(ticketChannel.Id);

        var header = await TicketManagerHelper.GetHeaderMessageAsync(ticketChannel);
        if (header is not null && header.Embeds.Count > 0)
            try
            {
                var umb = new DiscordMessageBuilder();
                umb.WithContent(header.Content);
                umb.AddEmbed(header.Embeds[0]);
                List<DiscordActionRowComponent> row =
                [
                    new DiscordActionRowComponent(TicketComponents.GetClosedTicketActionRow())
                ];
                umb.AddComponents(row);
                await header.ModifyAsync(umb);
            }
            catch (Exception e)
            {
                CurrentApplication.Logger.Warning(e, "Could not update the ticket header while closing");
            }

        await ticketChannel.SendMessageAsync(new DiscordEmbedBuilder
        {
            Description = "Ticket wird geschlossen..",
            Color = DiscordColor.Yellow
        });

        var progress = await ticketChannel.SendMessageAsync(new DiscordEmbedBuilder
        {
            Description = "Transcript wird gespeichert....",
            Color = DiscordColor.Yellow
        }.Build());

        var transcriptUrl = await TicketManagerHelper.GenerateTranscript(ticketChannel);
        await TicketManagerHelper.InsertTransscriptIntoDB(ticketChannel, TranscriptType.User, transcriptUrl);

        await progress.ModifyAsync(new DiscordEmbedBuilder
        {
            Description = "Transcript wurde gespeichert",
            Color = DiscordColor.Green
        }.Build());

        var ticketId = await TicketManagerHelper.GetTicketIdFromChannel(ticketChannel);
        await using (var cmd = Db.CreateCommand(
                         "UPDATE ticketstore SET closed = true, closed_at = @closed WHERE ticket_id = @ticket"))
        {
            cmd.Parameters.AddWithValue("closed", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            cmd.Parameters.AddWithValue("ticket", ticketId);
            await cmd.ExecuteNonQueryAsync();
        }

        await TicketCategoryService.LogEventAsync(ticketId, "closed", closedBy.Id, reason);

        var ticketUsers = await TicketManagerHelper.GetTicketUserIdsAsync(ticketChannel);
        await using (var roster = Db.CreateCommand(
                         "UPDATE ticketcache SET closed_users = @users WHERE ticket_id = @ticket"))
        {
            roster.Parameters.AddWithValue("users", ticketUsers.Select(id => (long)id).ToArray());
            roster.Parameters.AddWithValue("ticket", ticketId);
            await roster.ExecuteNonQueryAsync();
        }

        var ticketName = ticketChannel.Name;
        await ticketChannel.ModifyAsync(x => x.Name = $"closed-{ticketChannel.Name}");

        var description = $"Das Ticket wurde erfolgreich geschlossen!\n Geschlossen von " +
                          $"{closedBy.GetFormattedUserName()} ``{closedBy.Id}``";
        if (!string.IsNullOrWhiteSpace(reason)) description += $"\n{reason}";

        var closedEmbed = new DiscordEmbedBuilder
        {
            Title = "Ticket geschlossen",
            Description = description,
            Color = DiscordColor.Green
        }.Build();

        var deleteButton = new DiscordButtonComponent(ButtonStyle.Danger, "ticket_delete", "Ticket löschen ❌");
        var reopenButton =
            new DiscordButtonComponent(ButtonStyle.Secondary, TicketComponents.ReopenButtonId, "Wieder öffnen");
        var mb = new DiscordMessageBuilder();
        mb.WithContent(closedBy.Mention);
        mb.AddEmbed(closedEmbed);
        mb.AddComponents(deleteButton, reopenButton);
        await ticketChannel.SendMessageAsync(mb);

        foreach (var userId in ticketUsers)
        {
            DiscordMember? member = null;
            try
            {
                member = await ticketChannel.Guild.GetMemberAsync(userId);
            }
            catch (Exception)
            {
                // left the guild, nothing to strip and nobody to notify
            }

            await TicketManagerHelper.RemoveUserFromTicketAsync(ticketChannel, userId, member);
            if (member is not null)
                await TicketManagerHelper.SendTranscriptsToUser(member, transcriptUrl, RemoveType.Closed, ticketName);
        }
    }

    /// <summary>
    ///     Undoes a close: the ticket counts as open again, gets its name back and everyone who was in it
    ///     regains access. The limit on open tickets is deliberately not checked, this is a team action.
    /// </summary>
    public static async Task ReopenTicketAsync(ComponentInteractionCreateEventArgs args)
    {
        var interaction = args.Interaction;
        var channel = interaction.Channel;
        var member = await interaction.User.ConvertToMember(interaction.Guild);

        if (!await TicketAccess.MayHandleAsync(member, channel))
        {
            await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().WithContent("Du bist kein Teammitglied!").AsEphemeral());
            return;
        }

        var ticketId = await TicketManagerHelper.GetTicketIdFromChannel(channel);
        if (string.IsNullOrEmpty(ticketId))
        {
            await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .AddEmbed(EmbedGenerator.GetErrorEmbed("Zu diesem Channel gibt es kein Ticket mehr."))
                    .AsEphemeral());
            return;
        }

        // A ticket can carry more than one close message, so an older one must not reopen twice.
        if (await TicketManagerHelper.IsOpenTicket(channel))
        {
            await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent("Dieses Ticket ist bereits wieder offen.").AsEphemeral());
            return;
        }

        await interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);

        var ownerId = (ulong)await TicketManagerHelper.GetTicketOwnerFromChannel(channel);
        var roster = await LoadClosedRosterAsync(ticketId);
        if (roster.Count == 0 && ownerId > 0) roster.Add(ownerId);

        await using (var cmd = Db.CreateCommand(
                         "UPDATE ticketstore SET closed = false, closed_at = 0 WHERE ticket_id = @ticket"))
        {
            cmd.Parameters.AddWithValue("ticket", ticketId);
            await cmd.ExecuteNonQueryAsync();
        }

        // Without this the auto close task would see a ticket that has been idle since it was closed.
        await using (var cmd = Db.CreateCommand(
                         "UPDATE ticketcache SET last_activity = @now, reminder_sent_at = 0, closed_users = '{}' " +
                         "WHERE ticket_id = @ticket"))
        {
            cmd.Parameters.AddWithValue("now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            cmd.Parameters.AddWithValue("ticket", ticketId);
            await cmd.ExecuteNonQueryAsync();
        }

        if (channel.Name.StartsWith("closed-", StringComparison.OrdinalIgnoreCase))
            try
            {
                await channel.ModifyAsync(model =>
                {
                    model.Name = channel.Name["closed-".Length..];
                    model.AuditLogReason = $"Ticket wieder geöffnet von {interaction.User.GetFormattedUserName()}";
                });
            }
            catch (Exception e)
            {
                CurrentApplication.Logger.Warning(e, "Could not rename ticket channel {Channel} while reopening",
                    channel.Id);
            }

        var restored = 0;
        foreach (var userId in roster)
        {
            DiscordMember? ticketMember;
            try
            {
                ticketMember = await interaction.Guild.GetMemberAsync(userId);
            }
            catch (Exception)
            {
                continue;
            }

            await TicketManagerHelper.AddUserToTicket(interaction, channel, ticketMember);
            restored++;
        }

        await RestoreHeaderAsync(channel, ticketId);
        await DisableClosedMessageAsync(args);
        await TicketCategoryService.LogEventAsync(ticketId, "reopened", interaction.User.Id, null);

        var description = $"Das Ticket wurde von {interaction.User.Mention} wieder geöffnet.\n" +
                          $"{restored} von {roster.Count} Beteiligten haben wieder Zugriff.";

        var category = await TicketCategoryService.GetForChannelAsync(channel.Id);
        if (category is not null && ownerId > 0 && await FindBlockingTicketAsync(ownerId, category) is not null)
            description += "\nHinweis: Der Nutzer liegt damit über dem eingestellten Limit für offene Tickets.";

        var eb = new DiscordEmbedBuilder()
            .WithTitle("Ticket wieder geöffnet")
            .WithDescription(description)
            .WithColor(DiscordColor.Green)
            .WithFooter($"Ticket-ID: {ticketId}");

        var mb = new DiscordMessageBuilder().AddEmbed(eb);
        if (ownerId > 0) mb.WithContent($"<@{ownerId}>");
        await channel.SendMessageAsync(mb);

        await LogReopenAsync(channel, ticketId, interaction.User);
    }

    private static async Task<List<ulong>> LoadClosedRosterAsync(string ticketId)
    {
        await using var cmd = Db.CreateCommand("SELECT closed_users FROM ticketcache WHERE ticket_id = @ticket");
        cmd.Parameters.AddWithValue("ticket", ticketId);
        var result = await cmd.ExecuteScalarAsync();
        if (result is not long[] ids) return [];

        return [.. ids.Where(id => id > 0).Select(id => (ulong)id)];
    }

    private static async Task RestoreHeaderAsync(DiscordChannel channel, string ticketId)
    {
        var header = await TicketManagerHelper.GetHeaderMessageAsync(channel);
        if (header is null || header.Embeds.Count == 0) return;

        var claimed = false;
        await using (var cmd = Db.CreateCommand("SELECT claimed FROM ticketcache WHERE ticket_id = @ticket"))
        {
            cmd.Parameters.AddWithValue("ticket", ticketId);
            claimed = await cmd.ExecuteScalarAsync() is true;
        }

        try
        {
            var mb = new DiscordMessageBuilder();
            mb.WithContent(header.Content);
            mb.AddEmbed(header.Embeds[0]);
            List<DiscordActionRowComponent> row =
            [
                new DiscordActionRowComponent(claimed
                    ? TicketComponents.GetTicketClaimedActionRow()
                    : TicketComponents.GetTicketActionRow())
            ];
            mb.AddComponents(row);
            await header.ModifyAsync(mb);
        }
        catch (Exception e)
        {
            CurrentApplication.Logger.Warning(e, "Could not restore the ticket header while reopening");
        }
    }

    /// <summary>Nobody should delete a live ticket through the buttons of an outdated close message.</summary>
    private static async Task DisableClosedMessageAsync(ComponentInteractionCreateEventArgs args)
    {
        try
        {
            var mb = new DiscordMessageBuilder();
            mb.WithContent(args.Message.Content);
            if (args.Message.Embeds.Count > 0) mb.AddEmbed(args.Message.Embeds[0]);
            mb.AddComponents(
                new DiscordButtonComponent(ButtonStyle.Danger, "ticket_delete", "Ticket löschen ❌", true),
                new DiscordButtonComponent(ButtonStyle.Secondary, TicketComponents.ReopenButtonId, "Wieder öffnen",
                    true));
            await args.Message.ModifyAsync(mb);
        }
        catch (Exception e)
        {
            CurrentApplication.Logger.Warning(e, "Could not disable the close message after reopening");
        }
    }

    private static async Task LogReopenAsync(DiscordChannel channel, string ticketId, DiscordUser actor)
    {
        try
        {
            var logChannelId = ulong.Parse(BotConfig.GetConfig()["TicketConfig"]["LogChannelId"]);
            var logChannel = channel.Guild.GetChannel(logChannelId);

            var eb = new DiscordEmbedBuilder()
                .WithTitle("Ticket wieder geöffnet")
                .AddField(new DiscordEmbedField("Ticket", $"{channel.Mention} ``{ticketId}``", true))
                .AddField(new DiscordEmbedField("Durch", $"{actor.Mention} ``{actor.Id}``", true))
                .WithColor(DiscordColor.Green)
                .WithTimestamp(DateTime.Now);
            await logChannel.SendMessageAsync(eb);
        }
        catch (Exception e)
        {
            CurrentApplication.Logger.Warning(e, "Could not write the ticket reopen to the log channel");
        }
    }
}
