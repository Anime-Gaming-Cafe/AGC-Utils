#region

using AGC_Management.Enums;

#endregion

namespace AGC_Management.Entities.Metrics;

/// <summary>
///     Replacement for the gone presence intent: the most recent sign of life we have for a member and
///     where it came from. <see cref="LastSeenUnix" /> is 0 when nothing was ever recorded.
/// </summary>
public sealed record AvailabilityEstimate(ulong UserId, AvailabilitySignal Signal, long LastSeenUnix)
{
    public static AvailabilityEstimate None(ulong userId)
    {
        return new AvailabilityEstimate(userId, AvailabilitySignal.Unknown, 0);
    }

    /// <summary>
    ///     Purely time based, including for <see cref="AvailabilitySignal.Voice" /> - someone currently
    ///     sitting in a channel is stamped with "now", while a voice row dug out of the metrics log is
    ///     as old as it is and must not count as reachable.
    /// </summary>
    public bool IsWithin(TimeSpan window)
    {
        if (Signal == AvailabilitySignal.Unknown) return false;

        return DateTimeOffset.UtcNow.ToUnixTimeSeconds() - LastSeenUnix <= (long)window.TotalSeconds;
    }
}
