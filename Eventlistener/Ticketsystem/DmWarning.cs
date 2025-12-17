#region

#endregion

namespace AGC_Management.Eventlistener;

[EventHandler]
public class DmWarning : BaseCommandModule
{
    [Event]
    private Task MessageCreated(DiscordClient client, MessageCreateEventArgs args)
    {
        _ = Task.Run(async () =>
            {
                if (args.Channel.Type == ChannelType.Private && !args.Message.Author.IsBot)
                {
                    var supportlink = BotConfig.GetConfig()["TicketConfig"]["SupportLink"];
                    List<DiscordLinkButtonComponent> supportbutton =
					[
						new DiscordLinkButtonComponent(supportlink, "Zum Support")
                    ];
                    DiscordEmbed embed = new DiscordEmbedBuilder().WithTitle("AGC Support-System")
                        .WithDescription(
                            "Support wird nicht mehr per DM bearbeitet. \nBitte nutze den untenstehenden Button um zum Support zu gelangen!")
                        .WithColor(BotConfig.GetEmbedColor());
                    var msgbuilder = new DiscordMessageBuilder().AddComponents(supportbutton)
                        .AddEmbed(embed);
                    await args.Message.RespondAsync(msgbuilder);
                }
            }
        );
        return Task.CompletedTask;
    }
}