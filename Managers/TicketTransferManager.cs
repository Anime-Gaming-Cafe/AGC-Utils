#region

using AGC_Management.Entities.Ticket;
using AGC_Management.Services;
using AGC_Management.Utils;

#endregion

namespace AGC_Management.Managers;

/// <summary>
///     Hands a ticket to another team: the channel moves, gets the target category's name and role
///     access, and the claim is cleared so the new team picks it up themselves.
/// </summary>
public static class TicketTransferManager
{
    public const string SelectId = "ticket_transfer_select";

    private static NpgsqlDataSource Db =>
        CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

    public static async Task RenderSelectorAsync(DiscordInteraction interaction)
    {
        var member = await interaction.User.ConvertToMember(interaction.Guild);
        if (!await TicketAccess.MayHandleAsync(member, interaction.Channel))
        {
            await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().WithContent("Du bist kein Teammitglied!").AsEphemeral());
            return;
        }

        var current = await TicketCategoryService.GetForChannelAsync(interaction.Channel.Id);
        var targets = (await TicketAccess.VisibleCategoriesAsync(member, false))
            .Where(category => category.CustomId != current?.CustomId)
            .Take(25)
            .ToList();

        if (targets.Count == 0)
        {
            await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent("Es gibt keine andere Kategorie, an die du dieses Ticket übergeben kannst.")
                    .AsEphemeral());
            return;
        }

        var options = targets.Select(category => new DiscordStringSelectComponentOption(
            category.Label.Truncate(100), category.CustomId,
            string.IsNullOrWhiteSpace(category.Description) ? null : category.Description.Truncate(100))).ToList();

        var eb = new DiscordEmbedBuilder()
            .WithTitle("Ticket übergeben")
            .WithDescription("Wähle das Team aus, das dieses Ticket übernehmen soll. " +
                             "Der Channel wird verschoben, umbenannt und die Rechte werden getauscht.")
            .WithColor(BotConfig.GetEmbedColor());

        var selector = new DiscordStringSelectComponent("Wähle eine Kategorie", options, SelectId, 1, 1);
        await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().AddEmbed(eb).AddComponents(selector).AsEphemeral());
    }

    public static async Task TransferAsync(ComponentInteractionCreateEventArgs args)
    {
        var interaction = args.Interaction;
        var member = await interaction.User.ConvertToMember(interaction.Guild);
        if (!await TicketAccess.MayHandleAsync(member, interaction.Channel))
        {
            await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().WithContent("Du bist kein Teammitglied!").AsEphemeral());
            return;
        }

        var targetId = interaction.Data.Values.FirstOrDefault();
        var target = await TicketCategoryService.GetAsync(targetId);
        var channel = interaction.Channel;
        var source = await TicketCategoryService.GetForChannelAsync(channel.Id);

        if (target is null || !target.Enabled || target.CustomId == source?.CustomId)
        {
            await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .AddEmbed(EmbedGenerator.GetErrorEmbed("Diese Kategorie ist nicht verfügbar."))
                    .AsEphemeral());
            return;
        }

        if (!await TicketAccess.MayHandleAsync(member, target))
        {
            await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent("Du darfst Tickets nicht an diese Kategorie übergeben.").AsEphemeral());
            return;
        }

        await interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);

        var ticketId = await TicketManagerHelper.GetTicketIdFromChannel(channel);
        var number = await TicketCategoryService.NextTicketNumberAsync(target.CustomId);
        var parent = TicketManager.ResolveParent(interaction.Guild, target);
        var roleOverwrites = await TicketManager.BuildOverwritesAsync(interaction.Guild, target, parent);

        // Role access belongs to the new team, the per member entries of everyone in the ticket stay.
        var memberOverwrites = channel.PermissionOverwrites
            .Where(overwrite => overwrite.Type == OverwriteType.Member)
            .Select(overwrite => overwrite.ConvertToBuilder())
            .Where(builder => roleOverwrites.All(role => role.Target != builder.Target))
            .ToList();

        await channel.ModifyAsync(model =>
        {
            model.Name = $"{target.ChannelPrefix}-{number}";
            if (parent is not null) model.Parent = parent;
            if (roleOverwrites.Count > 0) model.PermissionOverwrites = [.. roleOverwrites, .. memberOverwrites];
            model.AuditLogReason = $"Ticket an {target.Label} übergeben von {interaction.User.GetFormattedUserName()}";
        });

        await using (var cmd = Db.CreateCommand("UPDATE ticketstore SET tickettype = @category WHERE ticket_id = @ticket"))
        {
            cmd.Parameters.AddWithValue("category", target.CustomId);
            cmd.Parameters.AddWithValue("ticket", ticketId);
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = Db.CreateCommand(
                         "UPDATE ticketcache SET claimed = false, claimed_from = 0 WHERE ticket_id = @ticket"))
        {
            cmd.Parameters.AddWithValue("ticket", ticketId);
            await cmd.ExecuteNonQueryAsync();
        }

        await ResetHeaderAsync(channel);
        await TicketCategoryService.LogEventAsync(ticketId, "transferred", interaction.User.Id,
            $"{source?.CustomId ?? "?"} -> {target.CustomId}");

        var pings = string.Join(" ", target.EffectivePingRoleIds.Select(id => $"<@&{id}>"));
        var eb = new DiscordEmbedBuilder()
            .WithTitle("Ticket übergeben")
            .WithDescription($"Dieses Ticket wurde von {interaction.User.Mention} an **{target.Label}** übergeben." +
                             (source is null ? "" : $"\nVorher: {source.Label}"))
            .WithColor(DiscordColor.Gold)
            .WithFooter($"Ticket-ID: {ticketId}");

        var mb = new DiscordMessageBuilder().AddEmbed(eb);
        if (!string.IsNullOrWhiteSpace(pings)) mb.WithContent(pings);
        await channel.SendMessageAsync(mb);

        await LogTransferAsync(channel, ticketId, source, target, interaction.User);
    }

    /// <summary>Re-enables the claim button, because the ticket is up for grabs again.</summary>
    private static async Task ResetHeaderAsync(DiscordChannel channel)
    {
        var header = await TicketManagerHelper.GetHeaderMessageAsync(channel);
        if (header is null || header.Embeds.Count == 0) return;

        try
        {
            var mb = new DiscordMessageBuilder();
            mb.WithContent(header.Content);
            mb.AddEmbed(header.Embeds[0]);
            List<DiscordActionRowComponent> row =
            [
                new DiscordActionRowComponent(Components.TicketComponents.GetTicketActionRow())
            ];
            mb.AddComponents(row);
            await header.ModifyAsync(mb);
        }
        catch (Exception e)
        {
            CurrentApplication.Logger.Warning(e, "Could not reset the ticket header after a transfer");
        }
    }

    private static async Task LogTransferAsync(DiscordChannel channel, string ticketId, TicketCategory? source,
        TicketCategory target, DiscordUser actor)
    {
        try
        {
            var logChannelId = ulong.Parse(BotConfig.GetConfig()["TicketConfig"]["LogChannelId"]);
            var logChannel = channel.Guild.GetChannel(logChannelId);

            var eb = new DiscordEmbedBuilder()
                .WithTitle("Ticket übergeben")
                .AddField(new DiscordEmbedField("Ticket", $"{channel.Mention} ``{ticketId}``", true))
                .AddField(new DiscordEmbedField("Von", source?.Label ?? "Unbekannt", true))
                .AddField(new DiscordEmbedField("An", target.Label, true))
                .AddField(new DiscordEmbedField("Durch", $"{actor.Mention} ``{actor.Id}``", true))
                .WithColor(DiscordColor.Gold)
                .WithTimestamp(DateTime.Now);
            await logChannel.SendMessageAsync(eb);
        }
        catch (Exception e)
        {
            CurrentApplication.Logger.Warning(e, "Could not write the ticket transfer to the log channel");
        }
    }
}
