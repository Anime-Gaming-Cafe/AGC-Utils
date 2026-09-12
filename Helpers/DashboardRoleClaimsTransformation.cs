#region

using System.Collections.Concurrent;
using System.Security.Claims;
using AGC_Management.Enums.Web;
using Microsoft.AspNetCore.Authentication;

#endregion

namespace AGC_Management.Utils;

public sealed class DashboardRoleClaimsTransformation : IClaimsTransformation
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(20);
    private static readonly ConcurrentDictionary<ulong, (AccessLevel Level, DateTime ExpiresUtc)> Cache = new();

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity { IsAuthenticated: true } identity)
            return principal;
        if (!ulong.TryParse(identity.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
            return principal;

        var level = await GetCachedAccessLevelAsync(userId);
        var existing = identity.FindFirst(ClaimTypes.Role);
        if (existing?.Value == level.ToString()) return principal;

        if (existing != null) identity.RemoveClaim(existing);
        identity.AddClaim(new Claim(ClaimTypes.Role, level.ToString()));
        return principal;
    }

    private static async Task<AccessLevel> GetCachedAccessLevelAsync(ulong userId)
    {
        if (Cache.TryGetValue(userId, out var cached) && cached.ExpiresUtc > DateTime.UtcNow)
            return cached.Level;

        var level = await AuthUtils.ResolveAccessLevelAsync(userId);
        Cache[userId] = (level, DateTime.UtcNow + CacheTtl);
        return level;
    }
}
