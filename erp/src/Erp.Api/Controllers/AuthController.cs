using System.Security.Claims;
using System.Security.Cryptography;
using Erp.Application.Common;
using Erp.Api.Security;
using Erp.Infrastructure.Auth;
using Erp.Infrastructure.Identity;
using Erp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Erp.Api.Controllers;

public sealed record LoginRequest(string Email, string Password);

public sealed record SessionInfo(int Id, string Email, string FullName, string[] Roles, string CsrfToken);

/// <summary>What the mobile app gets: a bearer token (no cookies, no CSRF) and who it belongs to.</summary>
public sealed record TokenInfo(string Token, DateTime ExpiresAt, int UserId, string Email, string FullName, string[] Roles, int? DriverId);

[ApiController]
[Route("api/v1/auth")]
public class AuthController(UserManager<AppUser> users, ITokenService tokens, AppDbContext db) : ControllerBase
{
    /// <summary>Checks email + password (with lockout). Returns the user, or the failure response to send back.</summary>
    private async Task<(AppUser? User, IActionResult? Fail)> CheckCredentialsAsync(LoginRequest? req)
    {
        const string bad = "Invalid email or password.";
        if (string.IsNullOrWhiteSpace(req?.Email) || string.IsNullOrEmpty(req.Password))
            return (null, BadRequest(ApiResponse.Fail(bad)));

        var user = await users.FindByEmailAsync(req.Email.Trim());
        if (user is null)
        {
            // Burn comparable time so unknown emails are not distinguishable by latency.
            users.PasswordHasher.HashPassword(new AppUser(), req.Password);
            return (null, Unauthorized(ApiResponse.Fail(bad)));
        }
        if (!user.IsActive) return (null, Unauthorized(ApiResponse.Fail(bad)));
        if (await users.IsLockedOutAsync(user))
            return (null, StatusCode(StatusCodes.Status429TooManyRequests, ApiResponse.Fail("Account temporarily locked. Try again later.")));

        if (!await users.CheckPasswordAsync(user, req.Password))
        {
            await users.AccessFailedAsync(user);
            return (null, Unauthorized(ApiResponse.Fail(bad)));
        }
        await users.ResetAccessFailedCountAsync(user);
        return (user, null);
    }

    [HttpPost("token")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Token([FromBody] LoginRequest req)
    {
        var (user, fail) = await CheckCredentialsAsync(req);
        if (user is null) return fail!;
        var roles = await users.GetRolesAsync(user);
        var (token, expires) = tokens.Create(user, roles, mobile: true);
        var driverId = await db.Drivers.AsNoTracking().Where(d => d.UserId == user.Id && d.IsActive).Select(d => (int?)d.Id).FirstOrDefaultAsync();
        return Ok(ApiResponse.Ok(new TokenInfo(token, expires, user.Id, user.Email!, user.FullName, roles.ToArray(), driverId)));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var (user, fail) = await CheckCredentialsAsync(req);
        if (user is null) return fail!;

        var roles = await users.GetRolesAsync(user);
        var (token, expires) = tokens.Create(user, roles);
        var csrf = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

        var secure = Request.IsHttps;
        Response.Cookies.Append(AuthCookies.Auth, token, new CookieOptions
        {
            HttpOnly = true, Secure = secure, SameSite = SameSiteMode.Strict, Expires = expires, Path = "/",
        });
        Response.Cookies.Append(AuthCookies.Csrf, csrf, new CookieOptions
        {
            HttpOnly = false, Secure = secure, SameSite = SameSiteMode.Strict, Expires = expires, Path = "/",
        });
        return Ok(ApiResponse.Ok(new SessionInfo(user.Id, user.Email!, user.FullName, roles.ToArray(), csrf)));
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public IActionResult Logout()
    {
        Response.Cookies.Delete(AuthCookies.Auth, new CookieOptions { Path = "/" });
        Response.Cookies.Delete(AuthCookies.Csrf, new CookieOptions { Path = "/" });
        return Ok(ApiResponse.Ok(new { }));
    }

    [HttpGet("me")]
    [Authorize]
    public IActionResult Me()
    {
        var csrf = Request.Cookies[AuthCookies.Csrf] ?? "";
        var info = new SessionInfo(
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!),
            User.FindFirstValue(ClaimTypes.Email) ?? "",
            User.FindFirstValue("full_name") ?? "",
            User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray(),
            csrf);
        return Ok(ApiResponse.Ok(info));
    }
}
