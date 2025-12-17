#region

using AGC_Management.Services;

#endregion

namespace AGC_Management.Utils;

public static class ReasonTemplateResolver
{
    public static async Task<string> Resolve(string template)
    {
        var replacements = await GetReplacements();
        foreach (var (key, value) in replacements) template = template.Replace("-" + key, value);

        return template;
    }

    public static async Task<Dictionary<string, string>> GetReplacements()
    {
        var replacements = new Dictionary<string, string>();
        var connectionString = DatabaseService.GetConnectionString();

        var conn = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = conn.CreateCommand("SELECT key, text FROM reasonmap");
        await using var reader = cmd.ExecuteReader();
        while (await reader.ReadAsync()) replacements[reader.GetString(0)] = reader.GetString(1);

        return replacements;
    }

    public static async Task<bool> AddReplacement(string key, string text)
    {
        var connectionString = DatabaseService.GetConnectionString();

        try
        {
            var conn = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

            await using (var checkCmd = conn.CreateCommand("SELECT COUNT(*) FROM reasonmap WHERE key = @key"))
            {
                checkCmd.Parameters.AddWithValue("key", key);
                var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
                if (exists > 0) return false;
            }

            await using (var cmd = conn.CreateCommand("INSERT INTO reasonmap (key, text) VALUES (@key, @text)"))
            {
                cmd.Parameters.AddWithValue("key", key);
                cmd.Parameters.AddWithValue("text", text);
                await cmd.ExecuteNonQueryAsync();
            }

            return true;
        }
        catch (NpgsqlException ex)
        {
            return false;
        }
    }

    public static async Task<bool> RemoveReplacement(string key)
    {
        var connectionString = DatabaseService.GetConnectionString();

        try
        {
            var conn = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
            await using var cmd = conn.CreateCommand("DELETE FROM reasonmap WHERE key = @key");
            cmd.Parameters.AddWithValue("key", key);

            var affectedRows = await cmd.ExecuteNonQueryAsync();
            return affectedRows > 0;
        }
        catch (NpgsqlException ex)
        {
            return false;
        }
    }
}