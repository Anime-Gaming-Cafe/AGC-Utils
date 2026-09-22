#region

using AGC_Management.Components;
using AGC_Management.Managers;

#endregion

namespace AGC_Management.Eventlistener;

[EventHandler]
public class TicketManagerEventHandler : BaseCommandModule
{
    [Event]
    public async Task ComponentInteractionCreated(DiscordClient client, ComponentInteractionCreateEventArgs e)
    {
        _ = Task.Run(async () =>
        {
            var cid = e.Interaction.Data.CustomId;
            if (cid == "ticket_claim")
                await TicketManagerHelper.ClaimTicket(e);
            else if (cid == "ticket_close")
                await TicketManager.CloseTicket(e, e.Channel);
            else if (cid == "ticket_delete")
                await TicketManagerHelper.DeleteTicket(e);
            else if (cid == "ticket_add_user")
                await TicketManagerHelper.AddUserToTicketSelector(e.Interaction);
            else if (cid == "adduser_selector")
                await TicketManagerHelper.AddUserToTicketSelector_Callback(e);
            else if (cid == "removeuser_selector")
                await TicketManagerHelper.RemoveUserFromTicketSelector_Callback(e);
            else if (cid == "ticket_remove_user")
                await TicketManagerHelper.RemoveUserFromTicketSelector(e.Interaction);
            else if (cid == "ticket_more")
                await TicketComponents.RenderMore(e);
            else if (cid == TicketComponents.TransferButtonId)
                await TicketTransferManager.RenderSelectorAsync(e.Interaction);
            else if (cid == TicketTransferManager.SelectId)
                await TicketTransferManager.TransferAsync(e);
            else if (cid == "ticket_userinfo")
                await TicketManagerHelper.UserInfo(e.Interaction);
            else if (cid == "userinfo_selector")
                await TicketManagerHelper.UserInfo_Callback(e);
            else if (cid == "ticket_flagtranscript")
                await TicketManagerHelper.GenerateTranscriptAndFlag(e.Interaction);
            else if (cid == "transcript_user_selector")
                await TicketManagerHelper.TranscriptFlag_Callback(e.Interaction, client);
            else if (cid == "generatetranscript")
                await TicketManagerHelper.GenerateTranscriptButton(e.Interaction);
            else if (cid == "ticket_snippets")
                await TicketManagerHelper.RenderSnippetSelector(e.Interaction);
            else if (cid.StartsWith("snippet_selector_"))
                await SnippetManagerHelper.SendSnippetAsync(e.Interaction);
            else if (cid.StartsWith("enable_noti_mode") || cid == "disable_notification")
                await NotificationManager.ChangeMode(e.Interaction);
            else if (cid == "manage_notification") await NotificationManager.RenderNotificationManager(e.Interaction);

            return Task.CompletedTask;
        });
    }
}