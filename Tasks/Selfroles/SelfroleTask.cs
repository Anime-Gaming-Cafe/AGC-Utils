#region

using AGC_Management.Components;
using AGC_Management.Services;

#endregion

namespace AGC_Management.Tasks;

public static class SelfroleTask
{
    public static async Task LaunchLoops()
    {
        await Task.Delay(TimeSpan.FromMinutes(2));
        while (true)
        {
            try
            {
                if (CurrentApplication.TargetGuild is not null && MemberCacheService.InitialDownloadComplete)
                {
                    await SelfroleComponents.RefreshPanelAsync();
                    await RunDailyAsync();
                }
            }
            catch (Exception e)
            {
                CurrentApplication.Logger.Error(e, "Selfroles: Durchlauf fehlgeschlagen");
            }

            await Task.Delay(TimeSpan.FromMinutes(2));
        }
    }

    /// <summary>Snapshot and cleanup once a day, marked in botsettings so a restart does not repeat it.</summary>
    private static async Task RunDailyAsync()
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var last = await RuntimeSettings.GetAsync(SelfroleService.Section, "LastStatsDate");
        if (last == today) return;

        await SelfroleService.CaptureStatsAsync();
        await SelfroleService.PurgeStickyAsync();
        await RuntimeSettings.SetAsync(SelfroleService.Section, "LastStatsDate", today);
    }
}
