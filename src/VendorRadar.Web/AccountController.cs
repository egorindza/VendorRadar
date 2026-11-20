using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace VendorRadar.Web;

[Route("account")]
public sealed class AccountController(WorkspaceDirectory directory) : Controller
{
    private static readonly PasswordHasher<string> Passwords = new();
    private static readonly string FallbackHash = Passwords.HashPassword("unused", Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));

    [HttpGet("login"), AllowAnonymous]
    public IActionResult Login(string? returnUrl = null, bool switchWorkspace = false)
    {
        if (User.Identity?.IsAuthenticated == true && !switchWorkspace) return LocalIfValid(returnUrl);
        ViewBag.ReturnUrl = returnUrl;
        ViewBag.Demo = directory.AllowDemo(HttpContext);
        return View();
    }

    [HttpPost("demo"), AllowAnonymous, EnableRateLimiting("login")]
    public async Task<IActionResult> Demo(string? account, string? returnUrl)
    {
        var match = directory.Find(account);
        if (!directory.AllowDemo(HttpContext) || match is not { Hash: null }) return NotFound();
        await SignInAsync(match);
        return LocalIfValid(returnUrl);
    }

    [HttpPost("login"), AllowAnonymous, EnableRateLimiting("login")]
    public async Task<IActionResult> SignInForm(string? account, string? password, string? returnUrl)
    {
        var user = directory.Find(account);
        var result = Passwords.VerifyHashedPassword(account ?? "", user?.Hash ?? FallbackHash, password ?? "");
        if (user is not { Hash: not null } || result == PasswordVerificationResult.Failed)
        {
            TempData["Error"] = "Неверное имя аккаунта или пароль.";
            return RedirectToAction(nameof(Login), new { returnUrl, switchWorkspace = true });
        }
        await SignInAsync(user);
        return LocalIfValid(returnUrl);
    }

    [HttpPost("logout"), Authorize]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [HttpGet("denied")]
    public IActionResult Denied()
    {
        Response.StatusCode = 403;
        return View("~/Views/Shared/Denied.cshtml");
    }

    private Task SignInAsync(WorkspaceDirectory.Account account) =>
        HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, CookieSessions.Principal(account));

    private IActionResult LocalIfValid(string? url) => LocalRedirect(Url.IsLocalUrl(url) ? url! : "/");
}
