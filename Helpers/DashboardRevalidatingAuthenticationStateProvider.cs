#region

using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.Extensions.Logging;

#endregion

namespace AGC_Management.Utils;

public sealed class DashboardRevalidatingAuthenticationStateProvider(ILoggerFactory loggerFactory)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromSeconds(30);

    protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState authenticationState,
        CancellationToken cancellationToken)
    {
        if (authenticationState.User.Identity is not ClaimsIdentity { IsAuthenticated: true } identity)
            return true;
        if (!ulong.TryParse(identity.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
            return true;

        var currentLevel = await AuthUtils.ResolveAccessLevelAsync(userId);
        return identity.FindFirst(ClaimTypes.Role)?.Value == currentLevel.ToString();
    }
}
