#region

using AGC_Management.Entities.Moderation;

#endregion

namespace AGC_Management.Services;

public static class UserWarnService
{
    private static NpgsqlDataSource Db =>
        CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

    /// <summary>
    ///     The user's currently active warns, newest first. Expired warns are already removed from this
    ///     table by the moderation expiry task, so every row here is still in effect.
    /// </summary>
    public static async Task<List<UserWarn>> GetWarnsAsync(ulong userId)
    {
        await using var cmd = Db.CreateCommand(
            "SELECT caseid, description, perma, datum FROM warns WHERE userid = @uid ORDER BY datum DESC");
        cmd.Parameters.AddWithValue("uid", (long)userId);

        var warns = new List<UserWarn>();

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            warns.Add(new UserWarn
            {
                CaseId = reader.IsDBNull(0) ? "" : reader.GetString(0),
                Description = reader.IsDBNull(1) ? "" : reader.GetString(1),
                IsPermanent = !reader.IsDBNull(2) && reader.GetBoolean(2),
                Timestamp = reader.IsDBNull(3) ? 0 : reader.GetInt64(3)
            });

        return warns;
    }
}
