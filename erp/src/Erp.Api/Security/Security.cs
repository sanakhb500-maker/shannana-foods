using System.Security.Cryptography;
using System.Text;
using Erp.Application.Common;
using Microsoft.AspNetCore.Http.Extensions;

namespace Erp.Api.Security;

public static class AuthCookies
{
    public const string Auth = "erp_auth";   // HttpOnly JWT
    public const string Csrf = "erp_csrf";   // readable by JS, echoed in X-CSRF-Token
    public const string CsrfHeader = "X-CSRF-Token";
}

public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public string? UserName => accessor.HttpContext?.User.Identity?.Name;
}

/// <summary>
/// Double-submit CSRF protection. A state-changing /api request that carries the auth cookie must
/// also send a matching X-CSRF-Token header (and, when present, a same-origin Origin header).
/// </summary>
public sealed class CsrfMiddleware(RequestDelegate next)
{
    private static readonly HashSet<string> Safe = ["GET", "HEAD", "OPTIONS", "TRACE"];

    public async Task InvokeAsync(HttpContext ctx)
    {
        var path = ctx.Request.Path;
        var needsCheck = path.StartsWithSegments("/api")
                         && !Safe.Contains(ctx.Request.Method)
                         && !path.StartsWithSegments("/api/v1/auth/login")
                         && ctx.Request.Cookies.ContainsKey(AuthCookies.Auth);

        if (needsCheck)
        {
            var cookie = ctx.Request.Cookies[AuthCookies.Csrf] ?? "";
            var header = ctx.Request.Headers[AuthCookies.CsrfHeader].ToString();
            var origin = ctx.Request.Headers.Origin.ToString();

            var sameOrigin = string.IsNullOrEmpty(origin)
                             || string.Equals(origin, $"{ctx.Request.Scheme}://{ctx.Request.Host}", StringComparison.OrdinalIgnoreCase);
            var tokenOk = cookie.Length > 0
                          && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(cookie), Encoding.UTF8.GetBytes(header));

            if (!sameOrigin || !tokenOk)
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                await ctx.Response.WriteAsJsonAsync(ApiResponse.Fail(MessageCatalog.Localize("CSRF validation failed.", LanguageMiddleware.Of(ctx))));
                return;
            }
        }
        await next(ctx);
    }
}
