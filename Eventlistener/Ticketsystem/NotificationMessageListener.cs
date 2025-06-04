#region

using AGC_Management.Enums;
using AGC_Management.Managers;
using AGC_Management.Utils;

#endregion

namespace AGC_Management.Eventlistener;

[EventHandler]
public class NotificationMessageListener : BaseCommandModule
{
    private static readonly long catid = long.Parse(BotConfig.GetConfig()["TicketConfig"]["SupportCategoryId"]);

    [Event]
    public static async Task MessageCreated(DiscordClient client, MessageCreateEventArgs e)
    {
        _ = Task.Run<Task>(async () =>
        {
            if (e.Guild == null) return;

            if (e.Message.Author.IsBot) return;

            if (e.Message.Channel.Parent == null) return;

            if ((long)e.Message.Channel.Parent.Id != catid) return;

            var openticket = await TicketManagerHelper.IsOpenTicket(e.Message.Channel);
            if (!openticket) return;

            if (TeamChecker.IsSupporter(await e.Message.Author.ConvertToMember(e.Message.Guild))) return;

            var subscribedStaffs = await NotificationManager.GetSubscribedStaffs(e.Channel);
            foreach (var staff in subscribedStaffs)
            {
                var currentmode = await NotificationManager.GetCurrentMode(e.Channel.Id, staff.Id);

                switch (currentmode)
                {
                    case NotificationMode.AlwaysBoth:
                        await NotifyUser(e.Message, staff);
                        await NotifyUserDM(e.Message, staff);
                        break;
                    case NotificationMode.AlwaysMention:
                        await NotifyUser(e.Message, staff);
                        break;
                    case NotificationMode.AlwaysDM:
                        await NotifyUserDM(e.Message, staff);
                        break;
                    case NotificationMode.Disabled:
                        break;
                    case NotificationMode.OnceBoth:
                        await NotifyUser(e.Message, staff);
                        await NotifyUserDM(e.Message, staff);
                        await NotificationManager.RemoveMode(e.Channel.Id, staff.Id);
                        break;
                    case NotificationMode.OnceMention:
                        await NotifyUser(e.Message, staff);
                        await NotificationManager.RemoveMode(e.Channel.Id, staff.Id);
                        break;
                    case NotificationMode.OnceDM:
                        await NotifyUserDM(e.Message, staff);
                        await NotificationManager.RemoveMode(e.Channel.Id, staff.Id);
                        break;
                }
            }
        });
    }

    public static async Task NotifyUser(DiscordMessage message, DiscordMember member)
    {
        var m = await message.Channel.SendMessageAsync(
            member.Mention + " Es gibt eine neue Nachricht in deinem Ticket!");
        await m.DeleteAsync();
    }

    public static async Task NotifyUserDM(DiscordMessage message, DiscordMember member)
    {
        var eb = new DiscordEmbedBuilder();
        eb.WithTitle("Neue Nachricht in deinem Ticket");
        eb.WithDescription($"In deinem Ticket {message.Channel.Mention} wurde eine neue Nachricht geschrieben!");
        eb.WithColor(BotConfig.GetEmbedColor());
        var toticketbutton = new DiscordLinkButtonComponent(
            $"https://discord.com/channels/{message.Guild.Id}/{message.Channel.Id}/{message.Id}", "Zum Ticket");
        var mb = new DiscordMessageBuilder();
        mb.AddEmbed(eb);
        mb.AddComponents(toticketbutton);
        try
        {
            await member.SendMessageAsync(mb);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }
}