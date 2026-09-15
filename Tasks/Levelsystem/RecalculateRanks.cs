#region

using AGC_Management.Utils;

#endregion

namespace AGC_Management.Tasks;

public static class RecalculateRanks
{
    public static async Task LaunchLoops()
    {
#if DEBUG
        CurrentApplication.Logger.Debug("In debug mode, not starting recalculate ranks task.");
#else
        await StartRecalculateRanks();
#endif
    }

    private static async Task StartRecalculateRanks()
    {
        await Task.Delay(TimeSpan.FromSeconds(5));
        while (true)
        {
            var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
            await using var cmd = con.CreateCommand("SELECT lastrecalc FROM levelingsettings");
            await using var reader = await cmd.ExecuteReaderAsync();
            await reader.ReadAsync();
            var lastrecalc = reader.GetInt64(0);
            await reader.CloseAsync();
            var currenttimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var difference = currenttimestamp - lastrecalc;

            if (difference >= 43200) await LevelUtils.RecalculateAllUserLevels();

            await Task.Delay(TimeSpan.FromHours(2));
        }
    }
}