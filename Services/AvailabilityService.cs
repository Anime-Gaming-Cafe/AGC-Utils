#region

using System.Collections.Concurrent;
using AGC_Management.Entities.Metrics;
using AGC_Management.Enums;

#endregion

namespace AGC_Management.Services;

/// <summary>
///     Estimates who is reachable right now without the presence intent, from three signals that all
///     work unprivileged: sitting in a voice channel, typing, and having just written something.
///     Deliberately feature-agnostic - ban requests use it to pick whom to ping, userinfo to show a
///     "last seen".
/// </summary>
public static class AvailabilityService
{
    private static readonly ConcurrentDictionary<ulong, (long Ts, AvailabilitySignal Signal)> _lastSeen = new();

    private static readonly TimeSpan PruneAge = TimeSpan.FromHours(1);
    private const int PruneEvery = 500;
    private static int _touchCounter;

    public static void Touch(ulong userId, AvailabilitySignal signal)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _lastSeen[userId] = (now, signal);

        if (Interlocked.Increment(ref _touchCounter) % PruneEvery == 0) Prune(now);
    }

    private static void Prune(long now)
    {
        var cutoff = now - (long)PruneAge.TotalSeconds;
        foreach (var (userId, entry) in _lastSeen)
            if (entry.Ts < cutoff)
                _lastSeen.TryRemove(userId, out _);
    }

    /// <summary>
    ///     Live estimate, no database. Voice beats everything else because it is the only signal that
    ///     stays true for as long as the person is actually there.
    /// </summary>
    public static AvailabilityEstimate Estimate(DiscordMember? member, ulong userId)
    {
        if (member?.VoiceState?.Channel is not null)
            return new AvailabilityEstimate(userId, AvailabilitySignal.Voice,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        return _lastSeen.TryGetValue(userId, out var entry)
            ? new AvailabilityEstimate(userId, entry.Signal, entry.Ts)
            : AvailabilityEstimate.None(userId);
    }

    public static bool IsLikelyAvailable(DiscordMember member, TimeSpan window)
    {
        return Estimate(member, member.Id).IsWithin(window);
    }

    public static List<DiscordMember> FilterAvailable(IEnumerable<DiscordMember> members, TimeSpan window)
    {
        return members.Where(m => IsLikelyAvailable(m, window)).ToList();
    }

    /// <summary>
    ///     Like <see cref="Estimate" />, but falls back to the metrics row logs when nothing is in memory -
    ///     survives a restart and reaches back over the whole recorded history.
    /// </summary>
    public static async Task<AvailabilityEstimate> GetLastSeenAsync(ulong userId, DiscordMember? member)
    {
        var live = Estimate(member, userId);
        if (live.Signal != AvailabilitySignal.Unknown) return live;

        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = con.CreateCommand(
            "SELECT (SELECT MAX(timestamp) FROM metrics_messages WHERE userid = @userid), " +
            "(SELECT MAX(timestamp) FROM metrics_voice WHERE userid = @userid)");
        cmd.Parameters.AddWithValue("userid", (long)userId);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return AvailabilityEstimate.None(userId);

        var lastMessage = reader.IsDBNull(0) ? 0 : reader.GetInt64(0);
        var lastVoice = reader.IsDBNull(1) ? 0 : reader.GetInt64(1);
        if (lastMessage == 0 && lastVoice == 0) return AvailabilityEstimate.None(userId);

        return lastVoice > lastMessage
            ? new AvailabilityEstimate(userId, AvailabilitySignal.Voice, lastVoice)
            : new AvailabilityEstimate(userId, AvailabilitySignal.Message, lastMessage);
    }

    public static string DescribeSignal(AvailabilitySignal signal)
    {
        return signal switch
        {
            AvailabilitySignal.Voice => "im Voice",
            AvailabilitySignal.Typing => "tippt gerade",
            AvailabilitySignal.Message => "Nachricht",
            _ => "unbekannt"
        };
    }
}
