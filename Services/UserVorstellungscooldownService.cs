#region

using AGC_Management.Eventlistener;

#endregion

namespace AGC_Management.Services;

public static class UserVorstellungscooldownService
{
    private static NpgsqlDataSource Db =>
        CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

    /// <summary>Null if the user has never posted a Vorstellung or their cooldown already expired.</summary>
    public static async Task<DateTimeOffset?> GetCooldownEndsAtAsync(ulong userId)
    {
        await using var cmd = Db.CreateCommand("SELECT time FROM vorstellungscooldown WHERE user_id = @uid");
        cmd.Parameters.AddWithValue("uid", (long)userId);

        var result = await cmd.ExecuteScalarAsync();
        if (result is not long lastPost) return null;

        var endsAt = DateTimeOffset.FromUnixTimeSeconds(lastPost + VorstellungscooldownListener.CooldownSeconds);
        return endsAt > DateTimeOffset.UtcNow ? endsAt : null;
    }
}
