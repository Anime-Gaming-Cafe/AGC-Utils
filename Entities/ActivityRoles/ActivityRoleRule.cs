#region

using AGC_Management.Enums.ActivityRoles;
using AGC_Management.Enums.Conditions;

#endregion

namespace AGC_Management.Entities.ActivityRoles;

public class ActivityRoleRule
{
    public string RuleId { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public ActivityRoleMetric Metric { get; set; } = ActivityRoleMetric.Messages;
    public ActivityRoleMode Mode { get; set; } = ActivityRoleMode.TopN;
    public ActivityRoleWindowType WindowType { get; set; } = ActivityRoleWindowType.Rolling;
    public int WindowDays { get; set; } = 7;
    public long? WindowStart { get; set; }
    public long? WindowEnd { get; set; }
    public long[] ScopeIds { get; set; } = [];
    public long[] ExcludeScopeIds { get; set; } = [];
    public long MinActivity { get; set; }
    public ulong ThresholdRoleId { get; set; }
    public long ThresholdValue { get; set; }
    public EligibilityComparator ThresholdComparator { get; set; } = EligibilityComparator.Gte;
    public bool AutoRevoke { get; set; } = true;
    public ulong AnnounceChannelId { get; set; }
    public string AnnounceMessage { get; set; } = "";

    /// <summary>0 = Ankündigung deaktiviert. Gilt für jedes Zeitfenster, auch rollierend/alltime.</summary>
    public int AnnounceIntervalDays { get; set; }

    public long LastAnnouncedAt { get; set; }

    /// <summary>
    ///     Order and presence of the pieces {rank1}/{rank2}/.../{winners} render with, e.g.
    ///     ["medal", "mention", "count", "role"]. See <see cref="ActivityRoleService.WinnerLineBlockKeys" />.
    /// </summary>
    public List<string> WinnerLineBlocks { get; set; } = ["medal", "mention", "count", "role"];

    public string MedalRank1 { get; set; } = "🥇";
    public string MedalRank2 { get; set; } = "🥈";
    public string MedalRank3 { get; set; } = "🥉";

    /// <summary>Rendered for rank 4+. Placeholder: {rank}.</summary>
    public string MedalOtherTemplate { get; set; } = "`#{rank}`";

    /// <summary>The raw metric count is divided by this before display (e.g. 60 to show voice minutes as hours).</summary>
    public long CountDivisor { get; set; } = 1;

    public string CountSuffix { get; set; } = "";
    public bool CountMonospace { get; set; } = true;

    /// <summary>
    ///     true (default): a candidate who left the server is skipped entirely, so the next eligible
    ///     candidate is promoted into their rank. false: their rank slot stays claimed (no promotion) -
    ///     they obviously still never receive the role, since granting requires being a member.
    /// </summary>
    public bool ExcludeLeftMembers { get; set; } = true;

    public bool CountMutedVoice { get; set; } = true;
    public bool CountDeafenedVoice { get; set; } = true;

    public ulong CreatedBy { get; set; }
    public long CreatedAt { get; set; }

    public List<ActivityRoleTier> Tiers { get; set; } = [];
}
