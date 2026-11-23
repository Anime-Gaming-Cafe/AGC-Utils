#region

using AGC_Management.Attributes;
using AGC_Management.Services;
using AGC_Management.Utils;
using DisCatSharp.ApplicationCommands;
using DisCatSharp.ApplicationCommands.Attributes;
using DisCatSharp.ApplicationCommands.Context;

#endregion

namespace AGC_Management.Commands.Entbannung;

public sealed class AbstimmungsCommand : ApplicationCommandsModule
{
    [ApplicationCommandRequireUnbanTeam]
    [SlashCommand("abstimmung", "Erstellt eine Entbannungsabstimmung.")]
    public static async Task AbstimmungCommand(InteractionContext ctx)
    {
        await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().AsEphemeral());

        await ctx.EditResponseAsync(
            new DiscordWebhookBuilder().WithContent("Prüfe, ob bereits eine Abstimmung läuft..."));

        if (await EntbannungService.GetAbstimmungByChannelAsync(ctx.Channel.Id) is not null)
        {
            await ctx.EditResponseAsync(
                new DiscordWebhookBuilder().WithContent("Es läuft bereits eine Abstimmung in diesem Channel!"));
            return;
        }

        await ctx.EditResponseAsync(
            new DiscordWebhookBuilder().WithContent("Keine Abstimmung gefunden. Erstelle eine neue Abstimmung..."));
        if (!ctx.Channel.Name.StartsWith("antrag-"))
        {
            await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent("Dieser Channel ist kein Antrag!"));
            return;
        }

        if (ctx.Channel.Name.Contains("-geschlossen"))
        {
            await ctx.EditResponseAsync(
                new DiscordWebhookBuilder().WithContent("Dieser Channel ist kein __offener__ Antrag!"));
            return;
        }

        var logchannel = ctx.Guild.GetChannel(EntbannungConfig.AbstimmungsLogChannelId);
        var logembed = new DiscordEmbedBuilder()
            .WithTitle("Antrag in die Abstimmung verschoben")
            .WithDescription(
                $"Antrag <#{ctx.Channel.Id}> (`#{ctx.Channel.Name}`) wurde durch {ctx.User.Mention} ({ctx.User.Id}) in die Abstimmung verschoben.");

        var votechannel = ctx.Guild.GetChannel(EntbannungConfig.AbstimmungsChannelId);
        var expiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 57600;

        var voteembed = EntbannungMessages.VoteRunning(ctx.Channel, expiresAt, 0, 0, 3,
            await EntbannungService.GetTeamMemberCountAsync(ctx.Client));
        var votemessage = await votechannel.SendMessageAsync(new DiscordMessageBuilder()
            .AddComponents(EntbannungMessages.VoteButtons(ctx.Channel.Id))
            .AddEmbed(voteembed)
            .WithContent(EntbannungService.GetTeamPing()));
        await EntbannungService.CreateAbstimmungAsync(ctx.Channel.Id, votemessage.Id, expiresAt, ctx.User.Id);
        await logchannel.SendMessageAsync(new DiscordMessageBuilder().AddEmbed(logembed));

        await ctx.EditResponseAsync(
            new DiscordWebhookBuilder().WithContent("Verschiebe Channel in die Abstimmungskategorie..."));
        var votecategory = await ctx.Client.GetChannelAsync(EntbannungConfig.VoteCategoryId);
        await ctx.Channel.ModifyAsync(x => x.Parent = votecategory);
        await Task.Delay(200);
        await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent("Verschoben!"));

        var notifyembed = new DiscordEmbedBuilder()
            .WithTitle("Status Update")
            .WithDescription("Lieber User, \n" +
                             "wir besprechen deinen Antrag nun intern. Du erhältst eine Rückmeldung, sobald die Entscheidung feststeht! \nDies dauert in der Regel 16 Stunden.")
            .WithColor(DiscordColor.Green)
            .WithFooter("AGC Entbannungssystem");
        await ctx.Channel.SendMessageAsync(notifyembed);
        await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent("Abstimmung erstellt!"));
    }
}
