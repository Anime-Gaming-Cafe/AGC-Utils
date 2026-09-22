#region

using AGC_Management.Utils;

#endregion

namespace AGC_Management.Components;

public class TicketComponents
{
    public const string ManageUsersButtonId = "ticket_manage_users";
    public const string TransferButtonId = "ticket_transfer";
    public const string ClaimButtonId = "ticket_claim";
    public const string ClaimReleaseId = "ticket_claim_release";
    public const string ClaimTakeId = "ticket_claim_take";
    public const string ReopenButtonId = "ticket_reopen";

    public static List<DiscordButtonComponent> GetTicketActionRow()
    {
        List<DiscordButtonComponent> buttons =
		[
			new DiscordButtonComponent(ButtonStyle.Danger, "ticket_close", "(Team) Ticket schließen ❌"),
            new DiscordButtonComponent(ButtonStyle.Primary, ClaimButtonId, "(Team) Ticket Claimen 👋"),
            new DiscordButtonComponent(ButtonStyle.Secondary, ManageUsersButtonId, "(Team) User verwalten 👥"),
            new DiscordButtonComponent(ButtonStyle.Secondary, TransferButtonId, "(Team) Ticket übergeben"),
            new DiscordButtonComponent(ButtonStyle.Success, "ticket_more", "(Team) Mehr...")
        ];
        return buttons;
    }

    public static List<DiscordButtonComponent> GetTicketClaimedActionRow()
    {
        List<DiscordButtonComponent> buttons =
		[
			new DiscordButtonComponent(ButtonStyle.Danger, "ticket_close", "(Team) Ticket schließen ❌"),
            new DiscordButtonComponent(ButtonStyle.Primary, ClaimButtonId, "(Team) Claim verwalten 👋"),
            new DiscordButtonComponent(ButtonStyle.Secondary, ManageUsersButtonId, "(Team) User verwalten 👥"),
            new DiscordButtonComponent(ButtonStyle.Secondary, TransferButtonId, "(Team) Ticket übergeben"),
            new DiscordButtonComponent(ButtonStyle.Success, "ticket_more", "(Team) Mehr...")
        ];
        return buttons;
    }

    public static List<DiscordButtonComponent> GetClosedTicketActionRow()
    {
        List<DiscordButtonComponent> buttons =
		[
			new DiscordButtonComponent(ButtonStyle.Danger, "ticket_close", "(Team) Ticket schließen ❌", true),
            new DiscordButtonComponent(ButtonStyle.Primary, ClaimButtonId, "(Team) Ticket Claimen 👋", true),
            new DiscordButtonComponent(ButtonStyle.Secondary, ManageUsersButtonId, "(Team) User verwalten 👥", true),
            new DiscordButtonComponent(ButtonStyle.Secondary, TransferButtonId, "(Team) Ticket übergeben", true),
            new DiscordButtonComponent(ButtonStyle.Success, "ticket_more", "(Team) Mehr...")
        ];
        return buttons;
    }

    public static List<DiscordButtonComponent> GetContactTicketActionRow()
    {
        List<DiscordButtonComponent> buttons =
		[
			new DiscordButtonComponent(ButtonStyle.Danger, "ticket_close", "(Team) Ticket schließen ❌"),
            new DiscordButtonComponent(ButtonStyle.Secondary, ManageUsersButtonId, "(Team) User verwalten 👥"),
            new DiscordButtonComponent(ButtonStyle.Secondary, TransferButtonId, "(Team) Ticket übergeben"),
            new DiscordButtonComponent(ButtonStyle.Success, "ticket_more", "(Team) Mehr...")
        ];
        return buttons;
    }

    public static async Task RenderMore(InteractionCreateEventArgs interactionCreateEvent)
    {
        var interaction = interactionCreateEvent.Interaction;
        var user = await interaction.User.ConvertToMember(interaction.Guild);

        if (!await TicketAccess.MayHandleAsync(user, interaction.Channel))
        {
            await interaction.CreateResponseAsync(
                InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().WithContent("Du bist kein Teammitglied!").AsEphemeral());
            return;
        }

        var buttons = new List<DiscordButtonComponent>
        {
            new(ButtonStyle.Primary, "ticket_userinfo", "Userinfo"),
            new(ButtonStyle.Primary, "ticket_flagtranscript", "Transcript Flaggen"),
            new(ButtonStyle.Primary, "generatetranscript", "Transcript erzeugen"),
            new(ButtonStyle.Primary, "ticket_snippets", "Snippet senden"),
            new(ButtonStyle.Success, "manage_notification", "Benachr. verwalten")
        };

        // Second row, because five is the per row limit. Claim management also lives here so it stays
        // reachable on tickets whose header still carries the old, disabled claim button.
        var responseBuilder = new DiscordInteractionResponseBuilder()
            .AddComponents(buttons)
            .AddComponents(new DiscordButtonComponent(ButtonStyle.Primary, ClaimButtonId, "Claim verwalten"))
            .AsEphemeral();
        await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, responseBuilder);
    }

    public static List<DiscordActionRowComponent> GetNotificationManagerButtons()
    {
        var buttons = new List<DiscordButtonComponent>
        {
            new(ButtonStyle.Danger, "disable_notification", "Deaktivieren", true),
            new(ButtonStyle.Primary, "enable_noti_mode1", "Einmalig Ping"),
            new(ButtonStyle.Primary, "enable_noti_mode2", "Einmalig DM"),
            new(ButtonStyle.Primary, "enable_noti_mode3", "Einmalig Ping + DM"),
            new(ButtonStyle.Primary, "enable_noti_mode4", "Bei jeder Nachricht Ping"),
            new(ButtonStyle.Primary, "enable_noti_mode5", "Bei jeder Nachricht DM"),
            new(ButtonStyle.Primary, "enable_noti_mode6", "Bei jeder Nachricht Ping + DM")
        };
        var actionRows = new List<DiscordActionRowComponent>
        {
            new(new List<DiscordButtonComponent> { buttons[0] }),
            new(buttons.GetRange(1, 3)),
            new(buttons.GetRange(4, 3))
        };
        return actionRows;
    }

    public static List<DiscordActionRowComponent> GetNotificationManagerButtonsEnabledNotify()
    {
        var buttons = new List<DiscordButtonComponent>
        {
            new(ButtonStyle.Danger, "disable_notification", "Deaktivieren"),
            new(ButtonStyle.Primary, "enable_noti_mode1", "Einmalig Ping"),
            new(ButtonStyle.Primary, "enable_noti_mode2", "Einmalig DM"),
            new(ButtonStyle.Primary, "enable_noti_mode3", "Einmalig Ping + DM"),
            new(ButtonStyle.Primary, "enable_noti_mode4", "Bei jeder Nachricht Ping"),
            new(ButtonStyle.Primary, "enable_noti_mode5", "Bei jeder Nachricht DM"),
            new(ButtonStyle.Primary, "enable_noti_mode6", "Bei jeder Nachricht Ping + DM")
        };
        var actionRows = new List<DiscordActionRowComponent>
        {
            new(new List<DiscordButtonComponent> { buttons[0] }),
            new(buttons.GetRange(1, 3)),
            new(buttons.GetRange(4, 3))
        };
        return actionRows;
    }
}