#region

using AGC_Management.Services;
using DisCatSharp.Interactivity.Extensions;

#endregion

namespace AGC_Management.Eventlistener.Birthday;

[EventHandler]
public sealed class BirthdayInteractionListener : BaseCommandModule
{
    [Event]
    public Task ComponentInteractionCreated(DiscordClient client, ComponentInteractionCreateEventArgs args)
    {
        var id = args.Interaction.Data.CustomId;
        if (id is null || !id.StartsWith("birthday:")) return Task.CompletedTask;

        _ = Task.Run(async () =>
        {
            try
            {
                if (await BirthdayService.RejectIfDisabledAsync(args.Interaction)) return;

                if (id == BirthdayService.SetButtonId) await HandleSet(client, args.Interaction);
                else if (id == BirthdayService.PingButtonId) await HandlePing(args.Interaction);
                else if (id == BirthdayService.CancelButtonId) await UpdatePanel(args.Interaction);
                else if (id.StartsWith(BirthdayService.ConfirmButtonPrefix))
                    await HandleConfirm(args.Interaction, id[BirthdayService.ConfirmButtonPrefix.Length..]);
            }
            catch (Exception e)
            {
                CurrentApplication.Logger.Error(e, "Birthday: Interaktion {Id} fehlgeschlagen", id);
            }
        });
        return Task.CompletedTask;
    }

    private static async Task HandleSet(DiscordClient client, DiscordInteraction interaction)
    {
        if (await BirthdayService.GetAsync(interaction.User.Id) is not null)
        {
            await UpdatePanel(interaction, "Dein Geburtstag ist bereits festgelegt.");
            return;
        }

        var modalId = $"birthday-modal-{Guid.NewGuid():N}";
        DiscordInteractionModalBuilder modal = new();
        modal.WithTitle("Geburtstag eingeben");
        modal.CustomId = modalId;
        modal.AddLabelComponent(new("Geburtstag (TT.MM)",
            component: new DiscordTextInputComponent(TextComponentStyle.Small, minLength: 3, maxLength: 5,
                placeholder: "21.05")));
        await interaction.CreateInteractionModalResponseAsync(modal);

        var result = await client.GetInteractivity().WaitForModalAsync(modalId, TimeSpan.FromMinutes(5));
        if (result.TimedOut) return;

        var submit = result.Result.Interaction;
        var input = (submit.Data.ModalComponents.OfType<DiscordLabelComponent>().First().Component as
            DiscordTextInputComponent)?.Value;

        if (!BirthdayService.TryParseDate(input, out var date))
        {
            await UpdatePanel(submit,
                "**Ungültiges Datum.** Bitte gib deinen Geburtstag im Format `TT.MM` ein, z.B. `21.05`.");
            return;
        }

        var (embed, buttons) = BirthdayService.BuildConfirm(submit.User, date);
        await submit.CreateResponseAsync(InteractionResponseType.UpdateMessage,
            new DiscordInteractionResponseBuilder().AddEmbed(embed).AddComponents(buttons));
    }

    private static async Task HandleConfirm(DiscordInteraction interaction, string raw)
    {
        if (!BirthdayService.TryParseDate(raw, out var date))
        {
            await UpdatePanel(interaction, "**Ungültiges Datum.**");
            return;
        }

        if (!await BirthdayService.InsertIfMissingAsync(interaction.User.Id, date))
        {
            await UpdatePanel(interaction, "Dein Geburtstag ist bereits festgelegt.");
            return;
        }

        await BirthdayService.GrantRoleIfTodayAsync(interaction.User.Id, date);
        await UpdatePanel(interaction,
            $"**Ersteinrichtung abgeschlossen!** Dein Geburtstag wurde auf **{BirthdayService.Humanize(date)}** gesetzt.");
    }

    private static async Task HandlePing(DiscordInteraction interaction)
    {
        var entry = await BirthdayService.GetAsync(interaction.User.Id);
        if (entry is null)
        {
            await UpdatePanel(interaction);
            return;
        }

        await BirthdayService.SetPingAsync(interaction.User.Id, !entry.Ping);
        await UpdatePanel(interaction,
            entry.Ping ? "Geburtstagsping wurde deaktiviert." : "Geburtstagsping wurde aktiviert.");
    }

    private static async Task UpdatePanel(DiscordInteraction interaction, string? notice = null)
    {
        var (embed, buttons) = await BirthdayService.BuildPanelAsync(interaction.User, notice);
        await interaction.CreateResponseAsync(InteractionResponseType.UpdateMessage,
            new DiscordInteractionResponseBuilder().AddEmbed(embed).AddComponents(buttons));
    }
}
