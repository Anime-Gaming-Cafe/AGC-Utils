#region

using AGC_Management.ApplicationSystem;
using AGC_Management.Services;
using AGC_Management.Utils;

#endregion

namespace AGC_Management.Eventlistener;

[EventHandler]
public sealed class OnApplyComponentInteraction : BaseCommandModule
{
    [Event]
    public Task ComponentInteractionCreated(DiscordClient client, ComponentInteractionCreateEventArgs args)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var customId = args.Interaction.Data.CustomId;

                if (customId != ApplyPanelCommands.MyApplicationsId && customId != ApplyPanelCommands.SelectorId)
                    return;

                await args.Interaction.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource,
                    new DiscordInteractionResponseBuilder().AsEphemeral());

                if (customId == ApplyPanelCommands.MyApplicationsId)
                {
                    await RespondAsync(args, "Meine Bewerbungen",
                        $"[Hier siehst du den Status deiner Bewerbungen]({ToolSet.GetDashboardUrl("apply/status")})",
                        DiscordColor.Green);
                    return;
                }

                var values = args.Interaction.Data.Values;
                if (values == null || !values.Any()) return;

                await HandleSelectAsync(args, values.First());
            }
            catch (Exception e)
            {
                CurrentApplication.Logger.Error(e, "TeamApplications: Panel-Interaktion fehlgeschlagen");
            }
        });

        return Task.CompletedTask;
    }

    private static async Task HandleSelectAsync(ComponentInteractionCreateEventArgs args, string positionId)
    {
        var position = await TeamApplicationService.GetPositionAsync(positionId);
        if (position is not { Active: true })
        {
            await RespondAsync(args, "Bewerbung", "Diese Position gibt es nicht mehr.", DiscordColor.Red);
            return;
        }

        var opening = await TeamApplicationService.ResolveOpeningAsync(position);
        if (!opening.CanApply)
        {
            var closedText = await TeamApplicationService.GetTextAsync("PanelClosedText",
                "Bewerbungen aktuell geschlossen");
            var description = $"**{position.PositionName}**: {closedText}.";

            if (opening.NextOpensAt > 0)
            {
                var opensAtText = await TeamApplicationService.GetTextAsync("PanelOpensAtText",
                    "Naechste Bewerbungsphase ab");
                description +=
                    $"\n{opensAtText} {ToolSet.GetFormattedTimeFromUnixAndRespectTimeZone(opening.NextOpensAt)}.";
            }

            await RespondAsync(args, "Bewerbung", description, DiscordColor.Red);
            return;
        }

        // The level gate never names a number, neither the required one nor the applicant's own.
        var level = await LevelUtils.GetLevel(args.Interaction.User.Id);
        if (!opening.Bypassed && level < position.MinLevel)
        {
            var gateText = await TeamApplicationService.GetTextAsync("LevelGateText",
                "Bring dich doch gerne etwas mehr in den Server ein, bevor du dich bewirbst.");
            await RespondAsync(args, "Bewerbung", gateText, DiscordColor.Orange);
            return;
        }

        var url = ToolSet.GetDashboardUrl($"apply/{position.PositionId}");
        await RespondAsync(args, "Bewerbung",
            $"[Klicke hier um dich für die Position ``{position.PositionName}`` zu bewerben]({url})",
            DiscordColor.Green);
    }

    private static async Task RespondAsync(ComponentInteractionCreateEventArgs args, string title,
        string description, DiscordColor color)
    {
        var embed = new DiscordEmbedBuilder()
            .WithTitle(title)
            .WithDescription(description)
            .WithColor(color);

        await args.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));
    }
}
