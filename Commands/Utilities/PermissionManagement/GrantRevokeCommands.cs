#region

using AGC_Management.Attributes;
using AGC_Management.Enums.ExtraPermissions;
using AGC_Management.Providers;
using AGC_Management.Services;
using AGC_Management.Utils;
using DisCatSharp.ApplicationCommands.Attributes;
using DisCatSharp.ApplicationCommands.Context;
using DisCatSharp.Exceptions;

#endregion

namespace AGC_Management.Commands.Perms;

public partial class Perms
{
    [ApplicationCommandRequireModerationTeam]
    [SlashCommand("grant", "Erteilt einem Mitglied eine Extra Permission")]
    public static Task Grant(InteractionContext ctx,
        [Option("member", "Das Mitglied")] DiscordUser user,
        [Autocomplete(typeof(ExtraPermissionAutocompleteProvider))]
        [Option("permission", "Die Permission", true)]
        string permName,
        [Option("duration", "Optionale Dauer, z.B. 7d, 12h oder 1d12h")]
        string? duration = null,
        [Option("reason", "Grund für die Vergabe")]
        string reason = "")
    {
        return SetOverrideAsync(ctx, user, permName, duration, reason, ExtraPermissionState.Granted);
    }

    [ApplicationCommandRequireModerationTeam]
    [SlashCommand("revoke", "Entzieht einem Mitglied eine Extra Permission")]
    public static Task Revoke(InteractionContext ctx,
        [Option("member", "Das Mitglied")] DiscordUser user,
        [Autocomplete(typeof(ExtraPermissionAutocompleteProvider))]
        [Option("permission", "Die Permission", true)]
        string permName,
        [Option("duration", "Optionale Dauer, z.B. 7d, 12h oder 1d12h")]
        string? duration = null,
        [Option("reason", "Grund für den Entzug")]
        string reason = "")
    {
        return SetOverrideAsync(ctx, user, permName, duration, reason, ExtraPermissionState.Revoked);
    }

    [ApplicationCommandRequireModerationTeam]
    [SlashCommand("reset", "Übergibt ein Mitglied wieder an den Automatismus")]
    public static async Task Reset(InteractionContext ctx,
        [Option("member", "Das Mitglied")] DiscordUser user,
        [Autocomplete(typeof(ExtraPermissionAutocompleteProvider))]
        [Option("permission", "Die Permission", true)]
        string permName,
        [Option("rearm-trigger", "Einen bereits ausgelösten Once-Trigger erneut scharf schalten")]
        bool rearmTrigger = false)
    {
        var permission = await ExtraPermissionService.GetPermissionAsync(permName);
        if (permission == null)
        {
            await RespondErrorAsync(ctx, $"Es gibt keine Permission mit dem Namen ``{permName}``.");
            return;
        }

        await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder()
                .WithContent("<a:loading_agc:1084157150747697203> Aktion wird ausgeführt..."));

        await ExtraPermissionService.ResetOverrideAsync(user.Id, permName, rearmTrigger);

        var member = await TryGetMemberAsync(ctx, user.Id);
        if (member != null) await ExtraPermissionService.EvaluateAsync(member, permission);

        var rearmText = rearmTrigger ? " Der Trigger wurde erneut scharf geschaltet." : "";
        await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
            $"<:success:1085333481820790944> **Erfolgreich!** {user.Mention} folgt für ``{permName}`` " +
            $"wieder dem Automatismus.{rearmText}"));
    }

    private static async Task SetOverrideAsync(InteractionContext ctx, DiscordUser user, string permName,
        string? duration, string reason, ExtraPermissionState state)
    {
        var permission = await ExtraPermissionService.GetPermissionAsync(permName);
        if (permission == null)
        {
            await RespondErrorAsync(ctx, $"Es gibt keine Permission mit dem Namen ``{permName}``.");
            return;
        }

        long expiresAt = 0;
        if (!string.IsNullOrWhiteSpace(duration))
        {
            var parsed = ToolSet.ParseDuration(duration);
            if (parsed == null)
            {
                await RespondErrorAsync(ctx,
                    "Die Dauer konnte nicht gelesen werden. Erlaubt sind z.B. ``30m``, ``12h``, ``7d``, ``2w`` oder ``1d12h``.");
                return;
            }

            expiresAt = DateTimeOffset.UtcNow.Add(parsed.Value).ToUnixTimeSeconds();
        }

        await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder()
                .WithContent("<a:loading_agc:1084157150747697203> Aktion wird ausgeführt..."));

        await ExtraPermissionService.SetOverrideAsync(user.Id, permName, state, expiresAt, ctx.User.Id, reason);

        var member = await TryGetMemberAsync(ctx, user.Id);
        var applied = false;
        if (member != null)
            applied = await ExtraPermissionService.ApplyAsync(member, permission,
                state == ExtraPermissionState.Granted
                    ? ExtraPermissionDecision.Grant
                    : ExtraPermissionDecision.Revoke);

        var verb = state == ExtraPermissionState.Granted ? "erteilt" : "entzogen";
        var expiryText = expiresAt > 0
            ? $" Läuft ab {Formatter.Timestamp(DateTimeOffset.FromUnixTimeSeconds(expiresAt), TimestampFormat.RelativeTime)}."
            : "";

        var memberNote = member == null
            ? " Das Mitglied ist nicht auf dem Server - die Entscheidung wird beim Beitritt angewendet."
            : applied
                ? ""
                : " Die Rolle war bereits im gewünschten Zustand.";

        await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
            $"<:success:1085333481820790944> **Erfolgreich!** ``{permName}`` wurde {user.Mention} {verb}." +
            $"{expiryText}{memberNote}"));
    }

    private static async Task<DiscordMember?> TryGetMemberAsync(InteractionContext ctx, ulong userId)
    {
        try
        {
            return await ctx.Guild.GetMemberAsync(userId);
        }
        catch (NotFoundException)
        {
            return null;
        }
    }
}
