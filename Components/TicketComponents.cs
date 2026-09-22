#region

using AGC_Management.Services;
using AGC_Management.Utils;

#endregion

namespace AGC_Management.Components;

public class TicketComponents
{
    public static List<DiscordButtonComponent> GetTicketActionRow()
    {
        List<DiscordButtonComponent> buttons =
		[
			new DiscordButtonComponent(ButtonStyle.Danger, "ticket_close", "(Team) Ticket schließen ❌"),
            new DiscordButtonComponent(ButtonStyle.Primary, "ticket_claim", "(Team) Ticket Claimen 👋"),
            new DiscordButtonComponent(ButtonStyle.Secondary, "ticket_add_user", "(Team) User hinzufügen 👥"),
            new DiscordButtonComponent(ButtonStyle.Secondary, "ticket_remove_user", "(Team) User entfernen 👤"),
            new DiscordButtonComponent(ButtonStyle.Success, "ticket_more", "(Team) Mehr...")
        ];
        return buttons;
    }

    public static List<DiscordButtonComponent> GetTicketClaimedActionRow()
    {
        List<DiscordButtonComponent> buttons =
		[
			new DiscordButtonComponent(ButtonStyle.Danger, "ticket_close", "(Team) Ticket schließen ❌"),
            new DiscordButtonComponent(ButtonStyle.Primary, "ticket_claim", "(Team) Ticket Claimen 👋", true),
            new DiscordButtonComponent(ButtonStyle.Secondary, "ticket_add_user", "(Team) User hinzufügen 👥"),
            new DiscordButtonComponent(ButtonStyle.Secondary, "ticket_remove_user", "(Team) User entfernen 👤"),
            new DiscordButtonComponent(ButtonStyle.Success, "ticket_more", "(Team) Mehr...")
        ];
        return buttons;
    }

    public static List<DiscordButtonComponent> GetClosedTicketActionRow()
    {
        List<DiscordButtonComponent> buttons =
		[
			new DiscordButtonComponent(ButtonStyle.Danger, "ticket_close", "(Team) Ticket schließen ❌", true),
            new DiscordButtonComponent(ButtonStyle.Primary, "ticket_claim", "(Team) Ticket Claimen 👋", true),
            new DiscordButtonComponent(ButtonStyle.Secondary, "ticket_add_user", "(Team) User hinzufügen 👥", true),
            new DiscordButtonComponent(ButtonStyle.Secondary, "ticket_remove_user", "(Team) User entfernen 👤", true),
            new DiscordButtonComponent(ButtonStyle.Success, "ticket_more", "(Team) Mehr...")
        ];
        return buttons;
    }

    public static List<DiscordButtonComponent> GetContactTicketActionRow()
    {
        List<DiscordButtonComponent> buttons =
		[
			new DiscordButtonComponent(ButtonStyle.Danger, "ticket_close", "(Team) Ticket schließen ❌"),
            new DiscordButtonComponent(ButtonStyle.Secondary, "ticket_add_user", "(Team) User hinzufügen 👥"),
            new DiscordButtonComponent(ButtonStyle.Secondary, "ticket_remove_user", "(Team) User entfernen 👤"),
            new DiscordButtonComponent(ButtonStyle.Success, "ticket_more", "(Team) Mehr...")
        ];
        return buttons;
    }

    public const string TransferButtonId = "ticket_transfer";

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

        var responseBuilder = new DiscordInteractionResponseBuilder().AddComponents(buttons).AsEphemeral();

        // A second row: five buttons is the per row limit, and handing a ticket to another team only
        // makes sense when there is another team to hand it to.
        var transferable = await TicketAccess.VisibleCategoriesAsync(user, false);
        var current = await TicketCategoryService.GetForChannelAsync(interaction.Channel.Id);
        if (transferable.Any(category => category.CustomId != current?.CustomId))
            responseBuilder.AddComponents(new DiscordButtonComponent(ButtonStyle.Secondary, TransferButtonId,
                "Ticket übergeben"));

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