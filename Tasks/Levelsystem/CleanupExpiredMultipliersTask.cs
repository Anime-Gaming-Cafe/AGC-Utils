#region

using AGC_Management.Utils;

#endregion

namespace AGC_Management.Tasks;

public static class CleanupExpiredMultipliersTask
{
    public static async Task Run()
    {
        await StartCleanupTask();
    }

    private static async Task StartCleanupTask()
    {
        await Task.Delay(TimeSpan.FromMinutes(1)); // Initial delay
        while (true)
        {
            try
            {
                CurrentApplication.Logger.Debug("Checking for expired timed multipliers...");
                await LevelUtils.CleanupExpiredTimedMultipliers();
            }
            catch (Exception e)
            {
                CurrentApplication.Logger.Error(e, "Error during timed multiplier cleanup");
                await ErrorReporting.SendErrorToDev(CurrentApplication.DiscordClient, null, e);
            }

            // Check every 5 minutes for expired multipliers
            await Task.Delay(TimeSpan.FromMinutes(5));
        }
    }
}