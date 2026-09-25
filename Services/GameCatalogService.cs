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
    ///     The catalogue row for an activity, created on first sighting. The id is settled right there and
    ///     never moves again - an application id that only turns up later is kept alongside instead, so a
    ///     game whose rows were already written under a hashed id does not split into two.
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
            ActivityId = applicationId != 0 ? (long)applicationId : StableId(key),
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
    ///     A deterministic id for games Discord gives no application id for. 63 bits so it stays a
    ///     positive BIGINT, and derived from the key so the same game always lands on the same number.
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
