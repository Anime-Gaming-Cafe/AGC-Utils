namespace AGC_Management.Entities.ApplicationSystem;

/// <summary>Result of <c>TeamApplicationService.ResolveOpeningAsync</c>.</summary>
public class TeamApplicationOpening
{
    public bool CanApply { get; init; }
    public TeamApplicationPhase? Phase { get; init; }
    public int QuestionSetVersion { get; init; }

    /// <summary>The position's debug switch is on: skip the level gate and the one per phase rule.</summary>
    public bool Bypassed { get; init; }

    /// <summary>Start of the next scheduled phase, or 0. Only set when applying is not possible.</summary>
    public long NextOpensAt { get; init; }
}
