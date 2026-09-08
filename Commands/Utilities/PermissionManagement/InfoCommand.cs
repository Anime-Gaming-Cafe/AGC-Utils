#region

using AGC_Management.Enums.ExtraPermissions;
using AGC_Management.Services;
using AGC_Management.Utils;
using DisCatSharp.ApplicationCommands.Attributes;
using DisCatSharp.ApplicationCommands.Context;
using DisCatSharp.Exceptions;

#endregion

namespace AGC_Management.Commands.PermissionManagement;

public partial class PermissionManagement
{
    [SlashCommand("info", "Zeigt die Extra Permissions eines Mitglieds", (long)Permissions.Administrator)]
    public static async Task Info(InteractionContext ctx,
        [Option("member", "Das Mitglied")] DiscordUser user)
    {
        DiscordMember? member = null;
        try
        {
            member = await ctx.Guild.GetMemberAsync(user.Id);
        }
        catch (NotFoundException)
        {
        }

        var statuses = await ExtraPermissionService.GetStatusAsync(user.Id, member);
        if (statuses.Count == 0)
        {
            await RespondErrorAsync(ctx, "Es sind noch keine Extra Permissions angelegt.");
            return;
        }

        var activeCount = statuses.Count(s => s.HasRole);
        var blocks = statuses.Select(s =>
        {
            var role = ctx.Guild.GetRole(s.Permission.RoleId);
            var roleText = role?.Mention ?? $"``{s.Permission.RoleId}``";
            var mode = ExtraPermissionFormatter.DescribeMode(s.Permission);
            var modeText = string.IsNullOrEmpty(mode) ? "" : $" · {mode}";
            var fired = s.MemberState.TriggerFired ? " · bereits ausgelöst" : "";

            var block = $"**{s.Permission.DisplayName}** ``{s.Permission.PermName}`` — {roleText}{modeText}{fired}\n" +
                        $"Status: {ExtraPermissionFormatter.DescribeStatus(s)}\n" +
                        $"Bedingungen:\n{ExtraPermissionFormatter.DescribeConditions(s.Permission, "> ")}\n" +
                        $"Rolle auf dem Mitglied: {(s.HasRole ? "ja" : "nein")}";

            if (!string.IsNullOrWhiteSpace(s.MemberState.Reason) &&
                s.MemberState.EffectiveState != ExtraPermissionState.Auto)
                block += $"\nGrund: {s.MemberState.Reason}";

            return block;
        });

        var description = JoinWithinLimit(blocks, out var shown, out var total);
        var footer = member == null
            ? "Mitglied ist nicht auf dem Server"
            : $"{activeCount} von {statuses.Count} Permissions aktiv";
        if (shown < total) footer += $" · {shown}/{total} angezeigt";

        var embed = new DiscordEmbedBuilder()
            .WithTitle($"Extra Permissions von {user.UsernameWithDiscriminator}")
            .WithDescription(description)
            .WithThumbnail(user.AvatarUrl)
            .WithColor(BotConfig.GetEmbedColor())
            .WithFooter(footer);

        await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().AddEmbed(embed).AsEphemeral());
    }
}
