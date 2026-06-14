#region

using AGC_Management.Services;

#endregion

namespace AGC_Management.Eventlistener.BoosterColors;

[EventHandler]
public sealed class BoosterColorInteractionListener : BaseCommandModule
{
    [Event]
    public Task ComponentInteractionCreated(DiscordClient client, ComponentInteractionCreateEventArgs args)
    {
        _ = Task.Run(async () =>
        {
            if (args.Interaction.Data.CustomId != BoosterColorService.SelectorCustomId) return;
            if (args.Guild == null || args.Guild != CurrentApplication.TargetGuild) return;

            DiscordMember member;
            try
            {
                member = await args.Guild.GetMemberAsync(args.User.Id);
            }
            catch
            {
                return;
            }

            if (BoosterColorService.IsStaff(member))
            {
                await Respond(args,
                    "Diese Funktion steht Teammitgliedern nicht zur Verfügung. Es hätte keine Wirkung.");
                return;
            }

            if (!await BoosterColorService.IsEligibleAsync(member))
            {
                await Respond(args,
                    "Du bist kein Booster! Du musst den Server boosten, um dieses Feature nutzen zu können.");
                return;
            }

            var value = args.Interaction.Data.Values?.FirstOrDefault();
            if (string.IsNullOrEmpty(value)) return;

            if (value == BoosterColorService.ResetValue)
            {
                await BoosterColorService.ResetColorAsync(member);
                await Respond(args, "Deine Farbe wurde erfolgreich auf Standard zurückgesetzt!");
                return;
            }

            if (!ulong.TryParse(value, out var roleId))
            {
                await Respond(args, "Ungültige Auswahl.");
                return;
            }

            var role = BoosterColorService.GetColorRoles(member.Guild).FirstOrDefault(r => r.Id == roleId);
            if (role == null)
            {
                await Respond(args, "Diese Farbe ist nicht mehr verfügbar.");
                return;
            }

            await BoosterColorService.ApplyColorAsync(member, role);
            await Respond(args, $"Deine Farbe wurde erfolgreich geändert! Du hast nun {role.Mention}");
        });

        return Task.CompletedTask;
    }

    private static Task Respond(ComponentInteractionCreateEventArgs args, string message)
    {
        var embed = new DiscordEmbedBuilder()
            .WithDescription(message)
            .WithColor(BotConfig.GetEmbedColor());

        return args.Interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().AddEmbed(embed).AsEphemeral());
    }
}
