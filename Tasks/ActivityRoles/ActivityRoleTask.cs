#region

using AGC_Management.Services;

#endregion

namespace AGC_Management.Tasks;

public static class ActivityRoleTask
{
    public static async Task Run()
    {
        await StartLoop();
    }

    private static async Task StartLoop()
    {
        await Task.Delay(TimeSpan.FromSeconds(30));

        while (true)
        {
            try
            {
                if (CurrentApplication.TargetGuild != null && MemberCacheService.InitialDownloadComplete)
                {
                    var rules = await ActivityRoleService.GetRulesAsync();
                    foreach (var rule in rules.Where(r => r.Enabled)) await ActivityRoleService.EvaluateRuleAsync(rule);
                }
            }
            catch (Exception e)
            {
                CurrentApplication.Logger.Error(e, "ActivityRoles: evaluation pass failed");
            }

            await Task.Delay(TimeSpan.FromMinutes(5));
        }
    }
}
