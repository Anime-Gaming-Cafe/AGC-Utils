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

    private static readonly ConcurrentDictionary<ulong, long> _persisted = new();

    private static readonly TimeSpan PruneAge = TimeSpan.FromHours(1);
    private const int PruneEvery = 500;
    private static int _touchCounter;

    /// <summary>
    ///     Typing fires every few seconds, so the table is written at most once per user and minute.
    ///     Memory stays the exact source; the row only has to be good enough to bridge a restart.
    /// </summary>
    private const int PersistThrottleSeconds = 60;

    public static void Touch(ulong userId, AvailabilitySignal signal)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _lastSeen[userId] = (now, signal);

        if (Interlocked.Increment(ref _touchCounter) % PruneEvery == 0) Prune(now);

        var lastWrite = _persisted.GetOrAdd(userId, 0);
        if (now - lastWrite < PersistThrottleSeconds) return;

        _persisted[userId] = now;
        _ = PersistAsync(userId, now, signal);
    }

    private static async Task PersistAsync(ulong userId, long timestamp, AvailabilitySignal signal)
    {
        try
        {
            var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
            await using var cmd = con.CreateCommand(
                "INSERT INTO member_lastseen (userid, last_seen, signal) VALUES (@userid, @last_seen, @signal) " +
                "ON CONFLICT (userid) DO UPDATE SET last_seen = EXCLUDED.last_seen, signal = EXCLUDED.signal");
            cmd.Parameters.AddWithValue("userid", (long)userId);
            cmd.Parameters.AddWithValue("last_seen", timestamp);
            cmd.Parameters.AddWithValue("signal", signal.ToString().ToLowerInvariant());
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception e)
        {
            CurrentApplication.Logger.Error(e, "Failed to persist last seen for {UserId}", userId);
        }
    }

    private static void Prune(long now)
    {
        var cutoff = now - (long)PruneAge.TotalSeconds;
        foreach (var (userId, entry) in _lastSeen)
            if (entry.Ts < cutoff)
                _lastSeen.TryRemove(userId, out _);

        foreach (var (userId, written) in _persisted)
            if (written < cutoff)
                _persisted.TryRemove(userId, out _);
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

    /// <summary>
    ///     Live first, then one query for everyone memory does not know - after a restart the whole
    ///     candidate list falls into that second group, and without it nobody would look reachable.
    /// </summary>
    public static async Task<List<DiscordMember>> FilterAvailableAsync(IEnumerable<DiscordMember> members,
        TimeSpan window)
    {
        var all = members.ToList();
        var available = all.Where(m => IsLikelyAvailable(m, window)).ToList();

        var unknown = all.Except(available).ToList();
        if (unknown.Count == 0) return available;

        var cutoff = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - (long)window.TotalSeconds;
        var seen = await GetPersistedSinceAsync(unknown.Select(m => m.Id), cutoff);
        available.AddRange(unknown.Where(m => seen.Contains(m.Id)));

        return available;
    }

    private static async Task<HashSet<ulong>> GetPersistedSinceAsync(IEnumerable<ulong> userIds, long cutoff)
    {
        var result = new HashSet<ulong>();
        var ids = userIds.Select(id => (long)id).ToArray();
        if (ids.Length == 0) return result;

        try
        {
            var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
            await using var cmd = con.CreateCommand(
                "SELECT userid FROM member_lastseen WHERE userid = ANY(@userids) AND last_seen >= @cutoff");
            cmd.Parameters.AddWithValue("userids", ids);
            cmd.Parameters.AddWithValue("cutoff", cutoff);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) result.Add((ulong)reader.GetInt64(0));
        }
        catch (Exception e)
        {
            CurrentApplication.Logger.Error(e, "Failed to read persisted last seen");
        }

        return result;
    }

    /// <summary>
    ///     Like <see cref="Estimate" />, but falls back to the metrics row logs when nothing is in memory -
    ///     survives a restart and reaches back over the whole recorded history.
    /// </summary>
    public static async Task<AvailabilityEstimate> GetLastSeenAsync(ulong userId, DiscordMember? member)
    {
        var live = Estimate(member, userId);
        if (live.Signal != AvailabilitySignal.Unknown) return live;

        // The stored row can lag behind the metric logs - somebody sitting in a channel for hours
        // raises no voice state event while the minute poll keeps writing - so all three are read at
        // once and the newest wins.
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = con.CreateCommand(
            "SELECT (SELECT last_seen FROM member_lastseen WHERE userid = @userid), " +
            "(SELECT signal FROM member_lastseen WHERE userid = @userid), " +
            "(SELECT MAX(timestamp) FROM metrics_messages WHERE userid = @userid), " +
            "(SELECT MAX(timestamp) FROM metrics_voice WHERE userid = @userid)");
        cmd.Parameters.AddWithValue("userid", (long)userId);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return AvailabilityEstimate.None(userId);

        var stored = reader.IsDBNull(0) ? 0 : reader.GetInt64(0);
        var storedSignal = reader.IsDBNull(1) || !Enum.TryParse<AvailabilitySignal>(reader.GetString(1), true,
            out var parsed)
            ? AvailabilitySignal.Message
            : parsed;
        var lastMessage = reader.IsDBNull(2) ? 0 : reader.GetInt64(2);
        var lastVoice = reader.IsDBNull(3) ? 0 : reader.GetInt64(3);

        var best = AvailabilityEstimate.None(userId);
        if (stored > best.LastSeenUnix) best = new AvailabilityEstimate(userId, storedSignal, stored);
        if (lastMessage > best.LastSeenUnix)
            best = new AvailabilityEstimate(userId, AvailabilitySignal.Message, lastMessage);
        if (lastVoice > best.LastSeenUnix) best = new AvailabilityEstimate(userId, AvailabilitySignal.Voice, lastVoice);

        return best;
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
