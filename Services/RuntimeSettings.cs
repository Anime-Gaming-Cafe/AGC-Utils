using System.Collections.Concurrent;

namespace AGC_Management.Services;

public static class RuntimeSettings
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private static readonly ConcurrentDictionary<(string section, string key), (string? value, DateTime expires)>
        _cache = new();

    public static async Task<string?> GetAsync(string section, string key)
    {
        if (_cache.TryGetValue((section, key), out var cached) && cached.expires > DateTime.UtcNow)
            return cached.value;

        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd =
            con.CreateCommand("SELECT value FROM botsettings WHERE section = @section AND key = @key");
        cmd.Parameters.AddWithValue("section", section);
        cmd.Parameters.AddWithValue("key", key);
        var result = await cmd.ExecuteScalarAsync();
        var value = result as string;

        _cache[(section, key)] = (value, DateTime.UtcNow + CacheTtl);
        return value;
    }

    public static async Task<bool> GetBoolAsync(string section, string key, bool fallback)
    {
        var raw = await GetAsync(section, key);
        return bool.TryParse(raw, out var parsed) ? parsed : fallback;
    }

    public static async Task<int> GetIntAsync(string section, string key, int fallback)
    {
        var raw = await GetAsync(section, key);
        return int.TryParse(raw, out var parsed) ? parsed : fallback;
    }

    public static async Task SetAsync(string section, string key, string value)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = con.CreateCommand(
            "INSERT INTO botsettings (section, key, value) VALUES (@section, @key, @value) " +
            "ON CONFLICT (section, key) DO UPDATE SET value = EXCLUDED.value");
        cmd.Parameters.AddWithValue("section", section);
        cmd.Parameters.AddWithValue("key", key);
        cmd.Parameters.AddWithValue("value", value);
        await cmd.ExecuteNonQueryAsync();

        _cache[(section, key)] = (value, DateTime.UtcNow + CacheTtl);
    }
}
