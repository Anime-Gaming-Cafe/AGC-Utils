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
        [Option("reason", "Grund für die Vergabe")]
        string reason,
        [Option("duration", "Optionale Dauer, z.B. 7d, 12h oder 1d12h")]
        string? duration = null)
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
        [Option("reason", "Grund für den Entzug")]
        string reason,
        [Option("duration", "Optionale Dauer, z.B. 7d, 12h oder 1d12h")]
        string? duration = null)
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
        [Option("reason", "Grund für das Zurücksetzen")]
        string reason,
        [Option("rearm-trigger", "Einen bereits ausgelösten Once-Trigger erneut scharf schalten")]
        bool rearmTrigger = false)
    {
        var permission = await ExtraPermissionService.GetPermissionAsync(permName);
        if (permission == null)
        {
            await RespondErrorAsync(ctx, $"Es gibt keine Permission mit dem Namen ``{permName}``.");
            return;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            await RespondErrorAsync(ctx, "Bitte gib einen Grund an.");
            return;
        }

        await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder()
                .WithContent("<a:loading_agc:1084157150747697203> Aktion wird ausgeführt..."));

        reason = await ReasonTemplateResolver.Resolve(reason);

        await ExtraPermissionService.ResetOverrideAsync(user.Id, permName, rearmTrigger);

        var member = await TryGetMemberAsync(ctx, user.Id);
        if (member != null) await ExtraPermissionService.EvaluateAsync(member, permission);

        var flagNote = await WritePermissionFlagAsync(ctx, user,
            $"[AUTO] Extra Permission \"{permission.DisplayName}\" auf Automatik zurückgesetzt - Grund: {reason}");

        var rearmText = rearmTrigger ? " Der Trigger wurde erneut scharf geschaltet." : "";
        await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
            $"<:success:1085333481820790944> **Erfolgreich!** {user.Mention} folgt für ``{permName}`` " +
            $"wieder dem Automatismus.{rearmText}{flagNote}"));
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

        if (string.IsNullOrWhiteSpace(reason))
        {
            await RespondErrorAsync(ctx, "Bitte gib einen Grund an.");
            return;
        }

        long expiresAt = 0;
        var durationText = "unbefristet";
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
            durationText = ExtraPermissionFormatter.DescribeDuration(parsed.Value);
        }

        await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder()
                .WithContent("<a:loading_agc:1084157150747697203> Aktion wird ausgeführt..."));

        reason = await ReasonTemplateResolver.Resolve(reason);

        await ExtraPermissionService.SetOverrideAsync(user.Id, permName, state, expiresAt, ctx.User.Id, reason);

        var member = await TryGetMemberAsync(ctx, user.Id);
        var applied = false;
        if (member != null)
            applied = await ExtraPermissionService.ApplyAsync(member, permission,
                state == ExtraPermissionState.Granted
                    ? ExtraPermissionDecision.Grant
                    : ExtraPermissionDecision.Revoke);

        var verb = state == ExtraPermissionState.Granted ? "erteilt" : "entzogen";

        var flagNote = await WritePermissionFlagAsync(ctx, user,
            $"[AUTO] Extra Permission \"{permission.DisplayName}\" {verb} ({durationText}) - Grund: {reason}");

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
            $"{expiryText}{memberNote}{flagNote}"));
    }

    /// <summary>
    ///     The override is already persisted when this runs, so a failing flag must not turn the whole
    ///     action into an error - it is reported in the success message instead.
    /// </summary>
    private static async Task<string> WritePermissionFlagAsync(InteractionContext ctx, DiscordUser user,
        string description)
    {
        try
        {
            var caseId = await ModerationHelper.PermissionFlag(user, ctx.User, description);
            return $" Flag ``{caseId}`` wurde angelegt.";
        }
        catch (Exception e)
        {
            CurrentApplication.Logger.Error(e, "Failed to write extra permission flag for {UserId}", user.Id);
            return " Der Auto-Flag konnte nicht geschrieben werden.";
        }
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
