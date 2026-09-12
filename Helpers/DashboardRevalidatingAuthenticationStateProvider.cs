#region

using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;

#endregion

namespace AGC_Management.Utils;

public sealed class DashboardAuthenticationStateProvider
    : AuthenticationStateProvider, IHostEnvironmentAuthenticationStateProvider, IDisposable
{
    private static readonly TimeSpan RevalidationInterval = TimeSpan.FromSeconds(30);
    private static readonly ClaimsPrincipal Anonymous = new(new ClaimsIdentity());

    private readonly ILogger _logger;
    private Task<AuthenticationState> _authenticationStateTask = Task.FromResult(new AuthenticationState(Anonymous));
    private CancellationTokenSource? _loopCts;

    public DashboardAuthenticationStateProvider(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<DashboardAuthenticationStateProvider>();
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync() => _authenticationStateTask;

    void IHostEnvironmentAuthenticationStateProvider.SetAuthenticationState(Task<AuthenticationState> authenticationStateTask)
    {
        _authenticationStateTask = authenticationStateTask;
        NotifyAuthenticationStateChanged(authenticationStateTask);

        var cts = new CancellationTokenSource();
        Interlocked.Exchange(ref _loopCts, cts)?.Cancel();
        _ = RunLoopAsync(authenticationStateTask, cts.Token);
    }

    private async Task RunLoopAsync(Task<AuthenticationState> stateTask, CancellationToken cancellationToken)
    {
        try
        {
            var state = await stateTask;
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(RevalidationInterval, cancellationToken);

                var updatedPrincipal = await BuildRefreshedPrincipalAsync(state.User);
                if (updatedPrincipal is null) continue;

                state = new AuthenticationState(updatedPrincipal);
                var newTask = Task.FromResult(state);
                _authenticationStateTask = newTask;
                NotifyAuthenticationStateChanged(newTask);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Aktualisieren des Dashboard-Zugriffslevels");
        }
    }

    private static async Task<ClaimsPrincipal?> BuildRefreshedPrincipalAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity { IsAuthenticated: true } identity) return null;
        if (!ulong.TryParse(identity.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId)) return null;

        var currentLevel = await AuthUtils.ResolveAccessLevelAsync(userId);
        var existing = identity.FindFirst(ClaimTypes.Role);
        if (existing?.Value == currentLevel.ToString()) return null;

        var updated = new ClaimsIdentity(identity);
        var role = updated.FindFirst(ClaimTypes.Role);
        if (role != null) updated.RemoveClaim(role);
        updated.AddClaim(new Claim(ClaimTypes.Role, currentLevel.ToString()));

        return new ClaimsPrincipal(updated);
    }

    public void Dispose() => _loopCts?.Cancel();
}
