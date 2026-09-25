#region

using AGC_Management.Entities.Metrics;
using AGC_Management.Entities.Selfroles;
using AGC_Management.Utils;

#endregion

namespace AGC_Management.Services;

/// <summary>
///     The catalogue of games the bot has seen. It exists so nobody has to guess what Discord calls a
///     game: every activity name that shows up lands here once and can then be bound to a selfrole
///     option by hand. The whole set is read every minute by the sampler, so it is cached.
/// </summary>
public static class GameCatalogService
{
    public const string Section = "GameMetrics";

    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
    private static readonly SemaphoreSlim LoadLock = new(1, 1);

    private static Dictionary<string, GameCatalogEntry>? _cache;
    private static DateTime _cacheExpires = DateTime.MinValue;

    private static NpgsqlDataSource Db =>
        CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

    public static void Invalidate()
    {
        _cacheExpires = DateTime.MinValue;
    }

    public static async Task<List<GameCatalogEntry>> GetAllAsync()
    {
        var all = await LoadAsync();
        return [.. all.Values.OrderBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)];
    }

    public static async Task<GameCatalogEntry?> GetAsync(string key)
    {
        var all = await LoadAsync();
        return all.GetValueOrDefault(key);
    }

    private static async Task<Dictionary<string, GameCatalogEntry>> LoadAsync()
    {
        if (_cache is not null && _cacheExpires > DateTime.UtcNow) return _cache;

        await LoadLock.WaitAsync();
        try
        {
            if (_cache is not null && _cacheExpires > DateTime.UtcNow) return _cache;

            var entries = new Dictionary<string, GameCatalogEntry>(StringComparer.Ordinal);
            await using var cmd = Db.CreateCommand(
                "SELECT activityname, activityid, displayname, applicationid, optionid, boundat, " +
                "firstseen, lastseen FROM metrics_activitymap");

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var entry = new GameCatalogEntry
                {
                    Key = reader.GetString(0),
                    ActivityId = reader.IsDBNull(1) ? 0 : reader.GetInt64(1),
                    DisplayName = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    ApplicationId = reader.IsDBNull(3) ? 0 : (ulong)reader.GetInt64(3),
                    OptionId = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    BoundAt = reader.IsDBNull(5) ? 0 : reader.GetInt64(5),
                    FirstSeen = reader.IsDBNull(6) ? 0 : reader.GetInt64(6),
                    LastSeen = reader.IsDBNull(7) ? 0 : reader.GetInt64(7)
                };

                entries[entry.Key] = entry;
            }

            _cache = entries;
            _cacheExpires = DateTime.UtcNow + CacheTtl;
            return entries;
        }
        finally
        {
            LoadLock.Release();
        }
    }

    /// <summary>
    ///     The catalogue row for an activity, created on first sighting.
    ///     The id is derived from the name, never from the application id: overlays and recorders report
    ///     their own application. Medal for instance sends 307998818547531777 for every game it records,
    ///     so "Apex Legends with Medal" and "Fortnite with Medal" would collapse into one identity.
    ///     The application id is kept as metadata only.
    /// </summary>
    public static async Task<GameCatalogEntry?> ResolveAsync(string activityName, ulong applicationId)
    {
        var key = ActivityMatcher.Key(activityName);
        if (key.Length == 0) return null;

        var all = await LoadAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (all.TryGetValue(key, out var known))
        {
            known.LastSeen = now;
            if (applicationId != 0 && known.ApplicationId == 0) known.ApplicationId = applicationId;
            return known;
        }

        var entry = new GameCatalogEntry
        {
            Key = key,
            ActivityId = StableId(key),
            DisplayName = activityName.Trim(),
            ApplicationId = applicationId,
            FirstSeen = now,
            LastSeen = now
        };

        await using (var cmd = Db.CreateCommand(
                         "INSERT INTO metrics_activitymap (activityname, activityid, displayname, " +
                         "applicationid, optionid, boundat, firstseen, lastseen) " +
                         "VALUES (@key, @activity, @display, @application, '', 0, @now, @now) " +
                         "ON CONFLICT (activityname) DO NOTHING"))
        {
            cmd.Parameters.AddWithValue("key", entry.Key);
            cmd.Parameters.AddWithValue("activity", entry.ActivityId);
            cmd.Parameters.AddWithValue("display", entry.DisplayName);
            cmd.Parameters.AddWithValue("application", (long)entry.ApplicationId);
            cmd.Parameters.AddWithValue("now", now);
            await cmd.ExecuteNonQueryAsync();
        }

        Invalidate();
        CurrentApplication.Logger.Information("Spielkatalog: {Game} neu aufgenommen", entry.DisplayName);

        // Another sampler pass may have won the insert, so the row that is actually stored wins.
        return await GetAsync(key) ?? entry;
    }

    /// <summary>Writes the sighting timestamps the sampler collected in one go.</summary>
    public static async Task TouchAsync(IReadOnlyCollection<GameCatalogEntry> seen)
    {
        if (seen.Count == 0) return;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await using var cmd = Db.CreateCommand(
            "UPDATE metrics_activitymap SET lastseen = @now, applicationid = @application " +
            "WHERE activityname = @key");
        cmd.Parameters.AddWithValue("now", now);
        var application = cmd.Parameters.AddWithValue("application", 0L);
        var key = cmd.Parameters.AddWithValue("key", "");

        foreach (var entry in seen)
        {
            application.Value = (long)entry.ApplicationId;
            key.Value = entry.Key;
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public static async Task BindAsync(string key, string optionId)
    {
        await using var cmd = Db.CreateCommand(
            "UPDATE metrics_activitymap SET optionid = @option, boundat = @now WHERE activityname = @key");
        cmd.Parameters.AddWithValue("key", key);
        cmd.Parameters.AddWithValue("option", optionId ?? "");
        cmd.Parameters.AddWithValue("now",
            string.IsNullOrEmpty(optionId) ? 0 : DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        await cmd.ExecuteNonQueryAsync();

        Invalidate();
    }

    /// <summary>Which games count as this option, for the read-only list in the category editor.</summary>
    public static async Task<List<GameCatalogEntry>> GetBoundToAsync(string optionId)
    {
        var all = await LoadAsync();
        return
        [
            .. all.Values
                .Where(entry => string.Equals(entry.OptionId, optionId, StringComparison.Ordinal))
                .OrderBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
        ];
    }

    /// <summary>
    ///     What the matcher thinks this game belongs to. Only ever a hint for the dashboard - nothing in
    ///     this file ever writes a binding on its own.
    /// </summary>
    public static (SelfroleOption Option, double Score)? Suggest(GameCatalogEntry entry,
        IReadOnlyList<SelfroleOption> options)
    {
        SelfroleOption? best = null;
        var bestScore = 0.0;

        foreach (var option in options)
        {
            if (option.MatchPatterns.Length == 0 && string.IsNullOrWhiteSpace(option.Label)) continue;

            var patterns = option.MatchPatterns.Length > 0 ? option.MatchPatterns : [option.Label];
            var score = ActivityMatcher.Score(patterns, entry.DisplayName);
            if (score <= bestScore) continue;

            bestScore = score;
            best = option;
        }

        return best is null || bestScore <= 0 ? null : (best, bestScore);
    }

    /// <summary>
    ///     Brings existing rows in line with the current normalisation: renames what now tokenises
    ///     differently, folds a row into its twin when both spellings of one game are already in the
    ///     catalogue, and renumbers ids that were once taken from the application id. Runs once at
    ///     startup and is a no-op afterwards. Bindings survive: a merge keeps the target's option, or
    ///     adopts the source's when the target has none.
    /// </summary>
    public static async Task RepairCatalogAsync()
    {
        var byKey = (await GetAllAsync()).ToDictionary(entry => entry.Key, StringComparer.Ordinal);
        var renamed = 0;
        var merged = 0;
        var renumbered = 0;

        foreach (var entry in byKey.Values.ToList())
        {
            var key = ActivityMatcher.Key(entry.DisplayName);
            if (key.Length == 0) continue;

            if (key != entry.Key)
            {
                if (byKey.TryGetValue(key, out var target))
                {
                    await MergeAsync(entry, target);
                    byKey.Remove(entry.Key);
                    merged++;
                    continue;
                }

                await RenameAsync(entry, key);
                byKey.Remove(entry.Key);
                entry.Key = key;
                entry.ActivityId = StableId(key);
                byKey[key] = entry;
                renamed++;
                continue;
            }

            var expected = StableId(entry.Key);
            if (entry.ActivityId == expected) continue;

            await SetActivityIdAsync(entry.Key, expected);
            renumbered++;
        }

        if (renamed + merged + renumbered == 0) return;

        Invalidate();
        CurrentApplication.Logger.Information(
            "Spielkatalog: {Renamed} umbenannt, {Merged} zusammengefuehrt, {Renumbered} neu nummeriert",
            renamed, merged, renumbered);
    }

    private static async Task RenameAsync(GameCatalogEntry entry, string key)
    {
        await using var cmd = Db.CreateCommand(
            "UPDATE metrics_activitymap SET activityname = @key, activityid = @activity " +
            "WHERE activityname = @old");
        cmd.Parameters.AddWithValue("old", entry.Key);
        cmd.Parameters.AddWithValue("key", key);
        cmd.Parameters.AddWithValue("activity", StableId(key));
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task MergeAsync(GameCatalogEntry source, GameCatalogEntry target)
    {
        var option = target.IsBound ? target.OptionId : source.OptionId;
        var boundAt = target.IsBound ? target.BoundAt : source.BoundAt;

        await using (var cmd = Db.CreateCommand(
                         "UPDATE metrics_activitymap SET activityid = @activity, optionid = @option, " +
                         "boundat = @boundat, applicationid = @application, firstseen = @firstseen, " +
                         "lastseen = @lastseen WHERE activityname = @key"))
        {
            cmd.Parameters.AddWithValue("key", target.Key);
            cmd.Parameters.AddWithValue("activity", StableId(target.Key));
            cmd.Parameters.AddWithValue("option", option);
            cmd.Parameters.AddWithValue("boundat", boundAt);
            cmd.Parameters.AddWithValue("application",
                (long)(target.ApplicationId != 0 ? target.ApplicationId : source.ApplicationId));
            cmd.Parameters.AddWithValue("firstseen", SmallestSeen(source.FirstSeen, target.FirstSeen));
            cmd.Parameters.AddWithValue("lastseen", Math.Max(source.LastSeen, target.LastSeen));
            await cmd.ExecuteNonQueryAsync();
        }

        await using var delete = Db.CreateCommand("DELETE FROM metrics_activitymap WHERE activityname = @key");
        delete.Parameters.AddWithValue("key", source.Key);
        await delete.ExecuteNonQueryAsync();

        if (source.IsBound && target.IsBound && source.OptionId != target.OptionId)
            CurrentApplication.Logger.Warning(
                "Spielkatalog: {Source} und {Target} zeigten auf verschiedene Optionen, {Option} behalten",
                source.DisplayName, target.DisplayName, option);
    }

    private static long SmallestSeen(long a, long b)
    {
        if (a == 0) return b;
        return b == 0 ? a : Math.Min(a, b);
    }

    private static async Task SetActivityIdAsync(string key, long activityId)
    {
        await using var cmd = Db.CreateCommand(
            "UPDATE metrics_activitymap SET activityid = @activity WHERE activityname = @key");
        cmd.Parameters.AddWithValue("key", key);
        cmd.Parameters.AddWithValue("activity", activityId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    ///     The identity of a game: 63 bits so it stays a positive BIGINT, derived from the normalised
    ///     name so the same game always lands on the same number.
    /// </summary>
    private static long StableId(string key)
    {
        ulong hash = 14695981039346656037;
        foreach (var c in key)
        {
            hash ^= c;
            hash *= 1099511628211;
        }

        return (long)(hash & 0x7FFFFFFFFFFFFFFF);
    }
}
