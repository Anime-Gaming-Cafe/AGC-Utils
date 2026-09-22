#region

using AGC_Management.Components;
using AGC_Management.Entities.Ticket;
using AGC_Management.Managers;
using AGC_Management.Services;
using AGC_Management.Utils;
using DisCatSharp.Interactivity.Extensions;

#endregion

namespace AGC_Management.Commands;

public class SupportPanel : BaseCommandModule
{
    [Command("initsupportpanel")]
    [RequireGuildOwner]
    public async Task InitSupportPanel(CommandContext ctx)
    {
        var embed = await SupportComponents.BuildPanelEmbedAsync();
        var msg = await ctx.Channel.SendMessageAsync(SupportComponents.BuildPanelMessage(embed));
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
            var customId = e.Interaction.Data.CustomId;

            if (customId == SupportComponents.OpenPanelButtonId)
            {
                if (!IsPanelChannel(e.Channel.Id)) return;

                await e.Interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                    await SupportComponents.BuildPickerAsync());
                return;
            }

            if (customId == SupportComponents.CategorySelectId)
            {
                var chosen = e.Interaction.Data.Values.FirstOrDefault();
                await StartTicketAsync(e.Interaction, client, await TicketCategoryService.GetAsync(chosen));
                return;
            }

            // Panel messages posted before the picker became a select menu still send one button per
            // category, so those ids keep working instead of going dead on deploy.
            if (customId.StartsWith(SupportComponents.LegacyOpenPrefix))
            {
                var chosen = customId[SupportComponents.LegacyOpenPrefix.Length..];
                await StartTicketAsync(e.Interaction, client, await TicketCategoryService.GetAsync(chosen));
            }
        });
    }

    private static bool IsPanelChannel(ulong channelId)
    {
        try
        {
            return ulong.Parse(BotConfig.GetConfig()["TicketConfig"]["SupportPanelChannel"]) == channelId;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static async Task StartTicketAsync(DiscordInteraction interaction, DiscordClient client,
        TicketCategory? category)
    {
        if (category is null || !category.Enabled)
        {
            await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .AddEmbed(EmbedGenerator.GetErrorEmbed("Diese Kategorie ist nicht mehr verfügbar."))
                    .AsEphemeral());
            return;
        }

        var questions = category.Questions.OrderBy(q => q.Position)
            .Take(TicketCategoryQuestion.MaxPerCategory).ToList();

        if (!category.IntakeEnabled || questions.Count == 0)
        {
            await TicketManager.OpenTicketAsync(interaction, category);
            return;
        }

        // A modal has to be the very first response to an interaction, so the limit is checked before
        // the form opens instead of after it was filled in.
        var blocking = await TicketManager.FindBlockingTicketAsync(interaction.User.Id, category);
        if (blocking is not null)
        {
            var eb = new DiscordEmbedBuilder
            {
                Title = "Fehler | Bereits ein Ticket geöffnet!",
                Description = $"Du hast bereits ein geöffnetes Ticket! -> <#{blocking}>",
                Color = DiscordColor.Red
            };
            var link = new DiscordLinkButtonComponent(
                $"https://discord.com/channels/{interaction.Guild.Id}/{blocking}", "Zum Ticket");
            await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().AddEmbed(eb).AddComponents(link).AsEphemeral());
            return;
        }

        var modalId = $"ticket_intake-{category.CustomId}-{TicketManagerHelper.GenerateTicketID(4)}";
        var modal = new DiscordInteractionModalBuilder();
        modal.WithTitle($"Ticket: {category.Label}".Truncate(45));
        modal.CustomId = modalId;

        foreach (var question in questions)
        {
            var maxLength = question.MaxLength > 0 ? question.MaxLength : question.IsLong ? 1000 : 200;
            var minLength = question.Required ? Math.Max(1, question.MinLength) : question.MinLength;
            modal.AddLabelComponent(new DiscordLabelComponent(question.Label.Truncate(45),
                component: new DiscordTextInputComponent(
                    question.IsLong ? TextComponentStyle.Paragraph : TextComponentStyle.Small,
                    placeholder: question.Placeholder.Truncate(100),
                    minLength: minLength,
                    maxLength: maxLength,
                    required: question.Required)));
        }

        await interaction.CreateInteractionModalResponseAsync(modal);

        var result = await client.GetInteractivity().WaitForModalAsync(modalId, TimeSpan.FromMinutes(10));
        if (result.TimedOut) return;

        var modalInteraction = result.Result.Interaction;
        await modalInteraction.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().AsEphemeral());

        var values = modalInteraction.Data.ModalComponents
            .OfType<DiscordLabelComponent>()
            .Select(label => (label.Component as DiscordTextInputComponent)?.Value ?? "")
            .ToList();

        var answers = questions.Select((question, index) => new TicketIntakeAnswer
        {
            QuestionId = question.Id,
            QuestionLabel = question.Label,
            Answer = index < values.Count ? values[index] : "",
            Position = index
        }).ToList();

        await TicketManager.OpenTicketAsync(modalInteraction, category, answers, true);
    }
}
