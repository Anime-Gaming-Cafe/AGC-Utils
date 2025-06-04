#region

using AGC_Management.Components;
using AGC_Management.Enums;
using AGC_Management.Managers;

#endregion

namespace AGC_Management.Commands;

public class SupportPanel : BaseCommandModule
{
    [Command("initsupportpanel")]
    [RequireGuildOwner]
    public async Task InitSupportPanel(CommandContext ctx)
    {
        var embed = new DiscordEmbedBuilder().WithTitle("AGC Support-System").WithDescription("""
                __Benötigst du Hilfe oder Support? Mach ein Ticket auf.__

                > Wann sollte ich ein Ticket öffnen?
                Wenn du irgendwelche Fragen hast oder irgendetwas unklar ist, du jemanden wegen Regelverstoß der Server Regeln oder der Discord Richtlinen melden möchtest!

                > Wie öffne ich ein Ticket?
                Wenn du ein Ticket öffnen willst, klicke unten auf "Ticket öffnen" und wähle danach eine der Kategorien aus, um was es geht. Danach wird ein Ticket mit dir erstellt und du kannst dein Anliegen schlidern.
                """).WithColor(BotConfig.GetEmbedColor())
            .WithFooter("Troll und absichtlicher Abuse ist zu unterlassen!")
            .Build();

        List<DiscordButtonComponent> buttons = new()
        {
            new DiscordButtonComponent(ButtonStyle.Danger, "selectticketcategory", "Ticket öffnen ✉️")
        };
        DiscordMessageBuilder msgb = new();
        msgb.AddEmbed(embed).AddComponents(buttons);
        var msg = await ctx.Channel.SendMessageAsync(msgb);
        BotConfig.SetConfig("TicketConfig", "SupportPanelMessage", msg.Id.ToString());
        BotConfig.SetConfig("TicketConfig", "SupportPanelChannel", ctx.Channel.Id.ToString());
        BotConfig.SetConfig("TicketConfig", "SupportGuild", ctx.Guild.Id.ToString());
    }
}

[EventHandler]
public class SupportPanelListener : SupportPanel
{
    [Event]
    public async Task ComponentInteractionCreated(DiscordClient client, ComponentInteractionCreateEventArgs e)
    {
        _ = Task.Run(async () =>
        {
            var PanelChannelId = ulong.Parse(BotConfig.GetConfig()["TicketConfig"]["SupportPanelChannel"]);
            if (e.Channel.Id == PanelChannelId && e.Interaction.Data.CustomId == "selectticketcategory")
            {
                List<DiscordButtonComponent> buttons = new();

                var sup_cats = await SupportComponents.GetSupportCategories();
                foreach (var cat in sup_cats)
                    buttons.Add(new DiscordButtonComponent(ButtonStyle.Primary, label: $"{cat.Value}",
                        customId: $"ticket_open_{cat.Key}"));

                DiscordEmbed embed = new DiscordEmbedBuilder()
                    .WithTitle("Wähle eine Supportkategorie aus")
                    .WithFooter("Wähle bitte die korrekte zu deinem Anliegen zutreffende Kategorie aus!")
                    .WithDescription(
                        "Wähle unten eine Supportkategorie aus. Dies hilft uns dein Ticket schneller zuzuordnen. \n" +
                        "Nach Auswahl der Kategorie wird ein Ticket erstellt, bitte schlildere anschließend im Ticket dein Anliegen.\n\n" +
                        "> Report / Melden \n" +
                        "Hier kannst du einen Benutzer melden der gegen Regeln verstößt oder anderweitig auffällt. \n\n" +
                        "> Support \n" +
                        "Hier kannst du dich bei generellen Anliegen melden").WithColor(BotConfig.GetEmbedColor());

                var ib = new DiscordInteractionResponseBuilder().AsEphemeral().AddComponents(buttons).AddEmbed(embed);
                await e.Interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, ib);
            }

            // handle ticket opening
            if (e.Interaction.Data.CustomId == "ticket_open_report")
                await TicketManager.OpenTicket(e.Interaction, TicketType.Report, client, TicketCreator.User);
            else if (e.Interaction.Data.CustomId == "ticket_open_support")
                await TicketManager.OpenTicket(e.Interaction, TicketType.Support, client, TicketCreator.User);

            return Task.CompletedTask;
        });
    }
}