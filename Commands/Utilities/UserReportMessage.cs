#region

using DisCatSharp.ApplicationCommands;
using DisCatSharp.ApplicationCommands.Attributes;
using DisCatSharp.ApplicationCommands.Context;
using DisCatSharp.Interactivity.Extensions;

#endregion

namespace AGC_Management.Commands;

public class UserReportMessage : ApplicationCommandsModule
{
    [ContextMenu(ApplicationCommandType.Message, "Nachricht ans Team melden")]
    public static async Task UserReportMessageCommand(ContextMenuContext ctx)
    {
        var randomid = new Random();
        var cid = randomid.Next(100000, 999999).ToString();

        DiscordInteractionModalBuilder modal = new();

        /* Disable this for now because Discord doenst support it yet
        List<DiscordStringSelectComponentOption> options = new();
        options.Add(new DiscordStringSelectComponentOption("Spam", "spam", "Spam"));
        options.Add(new DiscordStringSelectComponentOption("Beleidigung", "beleidigung", "Beleidigung"));
        options.Add(new DiscordStringSelectComponentOption("Werbung", "werbung", "Werbung"));
        options.Add(new DiscordStringSelectComponentOption("Sonstiges", "sonstiges", "Sonstiges"));
        modal.AddSelectComponent(new DiscordStringSelectComponent("report_reason", options, "Kategorie für die Meldung"));
        */

        var guild = ctx.Guild;
        var role = guild.GetRole(ulong.Parse(BotConfig.GetConfig()["ServerConfig"]["StaffRoleId"]));

        var member = await guild.GetMemberAsync(ctx.User.Id);
        ulong serverIdToSkip = 818699057878663168; // My test server

        var isEventler = false;

        foreach (var r in member.Roles)
            if (r.Name.Contains("event manager", StringComparison.InvariantCultureIgnoreCase))
                isEventler = true;

        if (member.Roles.Contains(role) && guild.Id != serverIdToSkip && isEventler)
        {
            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent("Moderative Teammitglieder können keine Nachrichten melden!")
                    .AsEphemeral());
            return;
        }


        if (ctx.User.Id == ctx.TargetMessage.Author.Id)
        {
            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().WithContent("Du kannst deine eigene Nachricht nicht melden!")
                    .AsEphemeral());
            return;
        }


        modal.WithTitle("Nachricht melden");
        modal.CustomId = cid;
        modal.AddLabelComponent(new("Weiter Infos zur Meldung", component: new DiscordTextInputComponent(TextComponentStyle.Paragraph, minLength: 4, maxLength: 400)));

        await ctx.CreateModalResponseAsync(modal);

        var interactivity = ctx.Client.GetInteractivity();
        var result = await interactivity.WaitForModalAsync(cid, TimeSpan.FromMinutes(5));

        if (result.TimedOut) return;


        var message = ctx.TargetMessage;
        var channel = ctx.Channel;

        var reportChannelId = ulong.Parse(BotConfig.GetConfig()["ModerationConfig"]["ReportLogId"]);
        var reportChannel = guild.GetChannel(reportChannelId);

        var teamChannelId = ulong.Parse(BotConfig.GetConfig()["ModerationConfig"]["TeamChatId"]);
        var teamChannel = guild.GetChannel(teamChannelId);

        var messagecontent = message.Content;
        var reporter = ctx.User;

        var reportedUser = message.Author;

        if (string.IsNullOrWhiteSpace(messagecontent))
            messagecontent = "Kein Textinhalt. Möglicherweise ein Bild oder Sticker oder ähnliches.";

        var targetMessageLink = message.JumpLink.ToString();
        var targetmessagebutton = new DiscordLinkButtonComponent(targetMessageLink, "Zur gemeldeten Nachricht");
        var embed = new DiscordEmbedBuilder()
            .WithTitle("Nachricht gemeldet")
            .WithDescription(
                $"**Gemeldeter User: {reportedUser.Mention} ({reportedUser.UsernameWithDiscriminator}) **```{reportedUser.Id}```\n**Gemeldete Nachricht:**```{messagecontent}```\n**Gemeldet von:**\n{ctx.User.Mention} / {ctx.User.Id}\n\n**Zusätzliche Infos:**\n```{(result.Result.Interaction.Data.ModalComponents.OfType<DiscordLabelComponent>().First().Component as DiscordTextInputComponent)?.Value}```")
            .WithColor(DiscordColor.Red)
            .WithFooter($"Gemeldet in #{channel.Name}")
            .Build();
        var mb = new DiscordMessageBuilder();
        mb.AddComponents(targetmessagebutton);
        mb.AddEmbed(embed);
        var m = await reportChannel.SendMessageAsync(mb);

        var targetuser = message.Author;

        await result.Result.Interaction.CreateResponseAsync(
            InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().WithContent("Danke fürs deinen Report!").AsEphemeral());

        await Task.Delay(500);

        await teamChannel.SendMessageAsync(
            $"**@here Neuer Report** \n{ctx.User.Mention} / {ctx.User.Id} hat eine Nachricht von {message.Author.Mention} / {message.Author.Id} gemeldet. {m.JumpLink}");
    }
}