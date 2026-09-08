#region

using AGC_Management.Entities.ExtraPermissions;
using AGC_Management.Enums.ExtraPermissions;
using AGC_Management.Services;

#endregion

namespace AGC_Management.Tasks;

public static class ExtraPermissionSyncTask
{
    private const int ReconcileEveryPasses = 15;
    private const int FullSweepEveryPasses = 60;

    private static readonly ExtraPermissionConditionType[] ReconcileTypes =
    [
        ExtraPermissionConditionType.Messages,
        ExtraPermissionConditionType.VoiceMinutes,
        ExtraPermissionConditionType.MembershipAge
    ];

    public static async Task LaunchLoops()
    {
        await StartSync();
    }

    private static async Task StartSync()
    {
        await Task.Delay(TimeSpan.FromSeconds(45));
        var pass = 0;

        while (true)
        {
            try
            {
                var guild = CurrentApplication.TargetGuild;
                if (guild != null)
                {
                    await ExtraPermissionService.EnsureMigratedAsync();
                    await ExpireOverrides(guild);

                    if (pass % ReconcileEveryPasses == 0) await ReconcileThresholds(guild);
                    if (pass % FullSweepEveryPasses == 0) await SweepDecayingConditions(guild);
                }
            }
            catch (Exception e)
            {
                CurrentApplication.Logger.Error(e, "ExtraPermissions: sync pass failed");
            }

            pass++;
            await Task.Delay(TimeSpan.FromMinutes(1));
        }
    }

    private static async Task ExpireOverrides(DiscordGuild guild)
    {
        var userIds = await ExtraPermissionService.GetMembersWithExpiredOverridesAsync();
        if (userIds.Count == 0) return;

        await ExtraPermissionService.ClearExpiredOverridesAsync();

        foreach (var userId in userIds)
        {
            if (!guild.Members.TryGetValue(userId, out var member)) continue;
            if (member.IsBot) continue;

            await ExtraPermissionService.EvaluateMemberAsync(member);
        }

        CurrentApplication.Logger.Information(
            $"ExtraPermissions: {userIds.Count} abgelaufene Overrides zurückgesetzt.");
    }

    /// <summary>
    ///     Candidates for the "at least N" conditions come straight out of the metrics tables, so a
    ///     threshold reconcile touches only the members who could possibly have crossed it.
    /// </summary>
    private static async Task ReconcileThresholds(DiscordGuild guild)
    {
        var permissions = await ExtraPermissionService.GetPermissionsAsync();
        var candidates = new HashSet<ulong>();

        foreach (var condition in permissions.SelectMany(p => p.Conditions))
        {
            if (condition.Negate) continue;

            if (condition.IsThreshold && condition.Comparator == ExtraPermissionComparator.Gte)
            {
                foreach (var id in await ExtraPermissionService.GetMembersOverThresholdAsync(guild, condition))
                    candidates.Add(id);
                continue;
            }

            if (condition.Type == ExtraPermissionConditionType.MembershipAge &&
                condition.Comparator == ExtraPermissionComparator.Gte)
            {
                var cutoff = DateTimeOffset.UtcNow.AddDays(-condition.Value);
                foreach (var member in guild.Members.Values)
                    if (!member.IsBot && member.JoinedAt != default && member.JoinedAt <= cutoff)
                        candidates.Add(member.Id);
            }
        }

        await EvaluateCandidates(guild, candidates);
    }

    /// <summary>
    ///     A "at most N" or negated threshold becomes true through inactivity, and inactivity raises no
    ///     event and produces no rows to query for. Those permissions therefore need a real sweep, which
    ///     runs on a much slower cadence and only when such a condition actually exists.
    /// </summary>
    private static async Task SweepDecayingConditions(DiscordGuild guild)
    {
        var permissions = await ExtraPermissionService.GetPermissionsAsync();
        var needsSweep = permissions.Any(p => p.Conditions.Any(c =>
            c.IsThreshold && (c.Comparator == ExtraPermissionComparator.Lte || c.Negate || c.WindowDays > 0)));

        if (!needsSweep) return;

        var candidates = guild.Members.Values.Where(m => !m.IsBot).Select(m => m.Id).ToHashSet();
        CurrentApplication.Logger.Information(
            $"ExtraPermissions: vollständiger Abgleich über {candidates.Count} Mitglieder.");

        await EvaluateCandidates(guild, candidates);
    }

    private static async Task EvaluateCandidates(DiscordGuild guild, HashSet<ulong> candidates)
    {
        foreach (var userId in candidates)
        {
            if (!guild.Members.TryGetValue(userId, out var member)) continue;
            if (member.IsBot) continue;

            await ExtraPermissionService.EvaluateMemberAsync(member, ReconcileTypes);
        }
    }
}
