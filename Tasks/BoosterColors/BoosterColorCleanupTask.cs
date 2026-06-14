#region

using AGC_Management.Services;

#endregion

namespace AGC_Management.Tasks;

public static class BoosterColorCleanupTask
{
    public static async Task LaunchLoops()
    {
        await StartCleanup();
    }

    private static async Task StartCleanup()
    {
        await Task.Delay(TimeSpan.FromSeconds(30));
        while (true)
        {
            try
            {
                var guild = CurrentApplication.TargetGuild;
                if (guild != null)
                {
                    var colorRoles = BoosterColorService.GetColorRoles(guild);
                    if (colorRoles.Count > 0)
                    {
                        var colorRoleIds = colorRoles.Select(r => r.Id).ToHashSet();
                        foreach (var member in guild.Members.Values)
                        {
                            if (member.IsBot) continue;
                            if (!member.Roles.Any(r => colorRoleIds.Contains(r.Id))) continue;
                            await BoosterColorService.CleanupMemberAsync(member);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                CurrentApplication.Logger.Error(e, "BoosterColors: periodic cleanup failed");
            }

            await Task.Delay(TimeSpan.FromMinutes(10));
        }
    }
}
