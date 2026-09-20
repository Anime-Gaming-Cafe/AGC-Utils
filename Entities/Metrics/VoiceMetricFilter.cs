namespace AGC_Management.Entities.Metrics;

/// <summary>
///     Which voice samples a metric query should leave out. Only meaningful for metrics_voice, so
///     callers can hand the same filter to both halves of a combined message+voice score.
/// </summary>
public sealed record VoiceMetricFilter(int[]? ExcludeVoiceStates = null, bool ExcludeSolo = false)
{
    public bool HasAny => ExcludeVoiceStates is { Length: > 0 } || ExcludeSolo;
}
