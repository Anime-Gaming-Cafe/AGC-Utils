#region

using AGC_Management.Components;
using AGC_Management.Services;

#endregion

namespace AGC_Management.Tasks;

/// <summary>
///     The safety net behind the panel's live updates. The GuildUpdated event and the dashboard already
///     queue a refresh, so this pass exists for what they cannot see: a banner swapped while the bot was
///     down, a message somebody deleted, and an edit that failed earlier. A pass with nothing to do costs
///     no API calls, because the refresh compares a hash before it touches Discord.
/// </summary>
public static class InfoPanelRefreshTask
{
    public static async Task LaunchLoops()
    {
        await Task.Delay(TimeSpan.FromMinutes(2));
        while (true)
        {
            try
            {
                await RunOnceAsync();
            }
            catch (Exception e)
            {
                CurrentApplication.Logger.Error(e, "InfoPanel: Refresh-Durchlauf fehlgeschlagen");
            }

            await Task.Delay(TimeSpan.FromMinutes(5));
        }
    }

    private static async Task RunOnceAsync()
    {
        if (CurrentApplication.TargetGuild is null) return;

        foreach (var panel in await InfoPanelService.GetAllAsync())
        {
            if (!panel.Enabled || !panel.IsPosted) continue;

            await InfoPanelComponents.RefreshPanelAsync(panel.Id);
        }
    }
}
