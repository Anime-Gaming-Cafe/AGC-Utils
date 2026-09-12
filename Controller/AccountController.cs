#region

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;

#endregion

namespace AGC_Management.Controller;

[Route("/[action]")]
public class AccountController : ControllerBase
{
    public AccountController(IDataProtectionProvider provider)
    {
        Provider = provider;
    }

    public IDataProtectionProvider Provider { get; }

    [HttpGet]
    public IActionResult Login(string returnUrl = "/")
    {
        if (!Url.IsLocalUrl(returnUrl)) returnUrl = "/";

        returnUrl = Uri.UnescapeDataString(returnUrl);


        return Challenge(new AuthenticationProperties { RedirectUri = returnUrl, IsPersistent = true }, "Discord");
    }

    [HttpGet]
    public async Task<IActionResult> LogOut(string returnUrl = "/")
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return LocalRedirect(returnUrl);
    }
}