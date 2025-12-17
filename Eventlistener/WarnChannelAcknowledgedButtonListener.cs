#region

#endregion

namespace AGC_Management.Eventlistener;

[EventHandler]
public class WarnChannelAcknowledgedButtonListener : BaseCommandModule
{
    [Event]
    public async Task ComponentInteractionCreated(DiscordClient client, ComponentInteractionCreateEventArgs args)
    {
        if (args.Channel.Name.StartsWith("warn-") && args.Channel.ParentId ==
            ulong.Parse(BotConfig.GetConfig()["TicketConfig"]["SupportCategoryId"]) && args.Id == "ackwarn")
            _ = Task.Run(async () =>
                {
                    var c_userid = args.Channel.Topic;
                    var c_user = await client.GetUserAsync(ulong.Parse(c_userid));
                    var a_user = args.User;
                    if (c_user.Id != a_user.Id)
                    {
                        await args.Interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                            new DiscordInteractionResponseBuilder()
                                .WithContent("Du kannst nur deine eigenen Warns bestätigen!")
                                .AsEphemeral());
                    }
                    else if (c_user.Id == a_user.Id)
                    {
                        var embed = args.Message.Embeds.First();
                        var component = args.Message.Components.First();
                        var mb = new DiscordMessageBuilder();
                        mb.AddEmbed(embed);
                        await args.Message.ModifyAsync(mb);
                        await args.Interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                            new DiscordInteractionResponseBuilder()
                                .WithContent("Der Warn wurde zur Kenntnis genommen!")
                                .AsEphemeral());
                        await Task.Delay(TimeSpan.FromSeconds(30));
                        await args.Channel.DeleteAsync(
                            "Warn wurde zur Kenntnis genommen. Dieser Channel wird nach 30 Sekunden gelöscht.");
                    }
                }
            );
    }
}