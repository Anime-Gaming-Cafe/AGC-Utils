#region

using AGC_Management.Entities.ApplicationSystem;
using AGC_Management.Enums.ApplicationSystem;
using AGC_Management.Utils;

#endregion

namespace AGC_Management.Services;

/// <summary>
///     Every outgoing message of the new application system. Internal markings deliberately have no entry
///     point here, they must never reach the applicant.
/// </summary>
public static class TeamApplicationNotificationService
{
    private static string StatusUrl => ToolSet.GetDashboardUrl("apply/status");

    private static async Task<DiscordUser?> TryGetUserAsync(ulong userId)
    {
        try
        {
            return await CurrentApplication.DiscordClient.GetUserAsync(userId);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static DiscordEmbedBuilder BaseEmbed(string title, string description, DiscordColor color)
    {
        return new DiscordEmbedBuilder()
            .WithTitle(title)
            .WithDescription(description)
            .WithColor(color)
            .WithFooter("AGC Bewerbungssystem", CurrentApplication.TargetGuild?.IconUrl ?? "");
    }

    /// <summary>Closed DMs are the normal case here, so the failure is reported back, not logged as an error.</summary>
    private static async Task<(bool delivered, string error)> TrySendDmAsync(ulong userId, DiscordEmbedBuilder embed)
    {
        try
        {
            var guild = CurrentApplication.TargetGuild;
            if (guild is not null && guild.Members.TryGetValue(userId, out var member))
            {
                await member.SendMessageAsync(embed);
                return (true, "");
            }

            var user = await TryGetUserAsync(userId);
            if (user == null) return (false, "Nutzer nicht gefunden");

            await user.SendMessageAsync(embed);
            return (true, "");
        }
        catch (Exception e)
        {
            return (false, e.GetType().Name);
        }
    }

    private static async Task PostToTeamChannelAsync(TeamApplication application, DiscordEmbedBuilder embed)
    {
        var position = await TeamApplicationService.GetPositionAsync(application.PositionId);
        if (position == null || position.NotifyChannelId == 0) return;

        try
        {
            var channel = CurrentApplication.TargetGuild?.GetChannel(position.NotifyChannelId);
            if (channel is null) return;

            await channel.SendMessageAsync(embed);
        }
        catch (Exception e)
        {
            CurrentApplication.Logger.Error(e, "TeamApplications: Team-Channel-Post fehlgeschlagen");
        }
    }

    public static async Task SendSubmitConfirmationAsync(TeamApplication application)
    {
        var user = await TryGetUserAsync(application.UserId);
        var title = await TeamApplicationService.GetTextAsync("DmSubmitTitle", "Bewerbung eingegangen");
        var body = await TeamApplicationService.GetTextAsync("DmSubmitText",
            "Deine Bewerbung ist bei uns eingegangen.");
        var text = await TeamApplicationTemplateResolver.ResolveAsync(body, application, user?.Username);

        var embed = BaseEmbed(title, $"{text}\n\n[Status deiner Bewerbungen ansehen]({StatusUrl})",
            DiscordColor.Green);
        await TrySendDmAsync(application.UserId, embed);
    }

    public static async Task PostNewApplicationAsync(TeamApplication application)
    {
        var intro = await TeamApplicationService.GetTextAsync("NotifyNewApplicationText",
            "Neue Bewerbung eingegangen.");

        var embed = BaseEmbed("Neue Bewerbung",
                $"{intro}\n\n**Position:** {application.PositionName}\n" +
                $"**Phase:** {application.PhaseName}\n" +
                $"**Bewerber:** <@{application.UserId}> (`{application.UserId}`)\n\n" +
                $"[Bewerbung ansehen]({ToolSet.GetDashboardUrl($"teamarea/applysystem/application/{application.ApplicationId}")})",
                DiscordColor.Gold)
            .WithTimestamp(DateTimeOffset.UtcNow);

        await PostToTeamChannelAsync(application, embed);
    }

    /// <summary>Sends the decision DM and records whether it arrived, so the team can follow up by hand.</summary>
    public static async Task SendDecisionAsync(TeamApplication application, string decisionText, ulong deciderId)
    {
        var user = await TryGetUserAsync(application.UserId);
        var decider = await TryGetUserAsync(deciderId);
        var accepted = application.Status == TeamApplicationStatus.Angenommen;

        var title = await TeamApplicationService.GetTextAsync(accepted ? "DmAcceptTitle" : "DmRejectTitle",
            accepted ? "Deine Bewerbung wurde angenommen" : "Deine Bewerbung wurde abgelehnt");
        var body = await TeamApplicationService.GetTextAsync(accepted ? "DmAcceptText" : "DmRejectText", "");

        var intro = await TeamApplicationTemplateResolver.ResolveAsync(body, application, user?.Username,
            decider?.Username);
        var reason = await TeamApplicationTemplateResolver.ResolveAsync(decisionText, application, user?.Username,
            decider?.Username);

        var description = intro;
        if (!string.IsNullOrWhiteSpace(reason)) description += $"\n\n{reason}";
        description += $"\n\n[Status deiner Bewerbungen ansehen]({StatusUrl})";

        var embed = BaseEmbed(title, description, accepted ? DiscordColor.Green : DiscordColor.Red);
        var (delivered, error) = await TrySendDmAsync(application.UserId, embed);
        await TeamApplicationService.SetDmResultAsync(application.ApplicationId, delivered, error);

        if (delivered) return;

        var failure = BaseEmbed("Entscheidung nicht zustellbar",
            $"Die Entscheidungs-DM an <@{application.UserId}> (`{application.UserId}`) konnte nicht zugestellt " +
            $"werden (`{error}`). Bitte fasst jemand per Ping nach.\n\n" +
            $"[Bewerbung ansehen]({ToolSet.GetDashboardUrl($"teamarea/applysystem/application/{application.ApplicationId}")})",
            DiscordColor.Orange);
        await PostToTeamChannelAsync(application, failure);
    }

    public static async Task SendGrantAsync(TeamApplicationReapplyGrant grant, string positionName)
    {
        var user = await TryGetUserAsync(grant.UserId);
        var title = await TeamApplicationService.GetTextAsync("DmGrantTitle", "Du darfst dich erneut bewerben");
        var body = await TeamApplicationService.GetTextAsync("DmGrantText", "");

        var stub = new TeamApplication { PositionId = grant.PositionId, PositionName = positionName };
        var text = await TeamApplicationTemplateResolver.ResolveAsync(body, stub, user?.Username);

        var description = text;
        if (!string.IsNullOrWhiteSpace(grant.Reason)) description += $"\n\n{grant.Reason}";
        description += $"\n\n[Jetzt erneut bewerben]({ToolSet.GetDashboardUrl($"apply/{grant.PositionId}")})";

        await TrySendDmAsync(grant.UserId, BaseEmbed(title, description, DiscordColor.Green));
    }
}
