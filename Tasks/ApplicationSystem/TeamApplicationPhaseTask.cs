#region

using AGC_Management.ApplicationSystem;
using AGC_Management.Enums.ApplicationSystem;
using AGC_Management.Services;

#endregion

namespace AGC_Management.Tasks;

/// <summary>
///     Flips phases whose optional opens_at / closes_at has been reached. Closing only stops new
///     applications, so nothing is auto-rejected here.
/// </summary>
public static class TeamApplicationPhaseTask
{
    public static async Task LaunchLoops()
    {
        await Run();
    }

    private static async Task Run()
    {
        await Task.Delay(TimeSpan.FromSeconds(40));

        while (true)
        {
            try
            {
                if (CurrentApplication.TargetGuild is not null && await SwitchDuePhases())
                    ApplyPanelCommands.QueueRefreshPanel();
            }
            catch (Exception e)
            {
                CurrentApplication.Logger.Error(e, "TeamApplications: Phasen-Loop fehlgeschlagen");
            }

            await Task.Delay(TimeSpan.FromMinutes(1));
        }
    }

    private static async Task<bool> SwitchDuePhases()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var changed = false;

        foreach (var phase in await TeamApplicationService.GetPhasesAsync())
            switch (phase.State)
            {
                case TeamApplicationPhaseState.Draft when phase.OpensAt > 0 && phase.OpensAt <= now:
                {
                    var result = await TeamApplicationService.OpenPhaseAsync(phase.PhaseId);
                    if (result != TeamApplicationPhaseOpenResult.Opened)
                    {
                        CurrentApplication.Logger.Warning(
                            $"TeamApplications: Phase {phase.PhaseId} konnte nicht geöffnet werden: {result}.");
                        continue;
                    }

                    CurrentApplication.Logger.Information(
                        $"TeamApplications: Phase {phase.Name} ({phase.PositionId}) geöffnet.");
                    changed = true;
                    break;
                }
                case TeamApplicationPhaseState.Open when phase.ClosesAt > 0 && phase.ClosesAt <= now:
                {
                    await TeamApplicationService.ClosePhaseAsync(phase.PhaseId);
                    CurrentApplication.Logger.Information(
                        $"TeamApplications: Phase {phase.Name} ({phase.PositionId}) geschlossen.");
                    changed = true;
                    break;
                }
            }

        return changed;
    }
}
