#region

using AGC_Management.Services;

#endregion

namespace AGC_Management.Tasks;

public static class BirthdayTask
{
    public static async Task LaunchLoops()
    {
        await Task.Delay(TimeSpan.FromMinutes(1));
        while (true)
        {
            try
            {
                if (CurrentApplication.TargetGuild is not null && MemberCacheService.InitialDownloadComplete)
                    await BirthdayService.RunDailyAsync();
            }
            catch (Exception e)
            {
                CurrentApplication.Logger.Error(e, "Birthday: Durchlauf fehlgeschlagen");
            }

            await Task.Delay(TimeSpan.FromSeconds(60));
        }
    }
}
