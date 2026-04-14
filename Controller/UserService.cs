#region

using System.Security.Claims;
using AGC_Management.Entities.Web;
using AGC_Management.Utils;

#endregion

namespace AGC_Management.Controller;

public class UserService
{
    private static readonly HttpClient client = new();

    public bool IsAuthenticated(HttpContext httpContext)
    {
        return httpContext.User.Identity.IsAuthenticated;
    }

    public ulong? GetUserId(HttpContext httpContext)
    {
        if (!httpContext.User.Identity.IsAuthenticated) return null;

        var claims = httpContext.User.Claims;
        return ulong.Parse(claims.First(x => x.Type == ClaimTypes.NameIdentifier).Value);
    }


    /// <summary>
    ///     Parses the user's discord claim for their `identify` information
    /// </summary>
    /// <param name="httpContext"></param>
    /// <returns></returns>
    public async Task<DiscordUserWebClaim> GetInfo(HttpContext httpContext)
    {
        if (!httpContext.User.Identity.IsAuthenticated) return null;

        var claims = httpContext.User.Claims;
        bool? verified;
        if (bool.TryParse(claims.FirstOrDefault(x => x.Type == "urn:discord:verified")?.Value, out var _verified))
            verified = _verified;
        else
            verified = null;

        var Role = await AuthUtils.RetrieveRole(ulong.Parse(claims.First(x => x.Type == ClaimTypes.NameIdentifier)
            .Value));
        var displayname = await AuthUtils.RetrieveDisplayName(ulong.Parse(claims
            .First(x => x.Type == ClaimTypes.NameIdentifier)
            .Value));
        var fullQualifiedDiscordName = claims.First(x => x.Type == "FullQualifiedDiscordName").Value;
        var userClaim = new DiscordUserWebClaim
        {
            UserId = ulong.Parse(claims.First(x => x.Type == ClaimTypes.NameIdentifier).Value),
            Name = claims.First(x => x.Type == ClaimTypes.Name).Value,
            DisplayName = displayname,
            Discriminator = claims.First(x => x.Type == "urn:discord:discriminator").Value,
            Avatar = claims.First(x => x.Type == "urn:discord:avatar").Value,
            WebRole = Role,
            FullQualifiedDiscordName = fullQualifiedDiscordName,
            Email = claims.FirstOrDefault(x => x.Type == ClaimTypes.Email)?.Value,
            Verified = verified
        };

        return userClaim;
    }
}