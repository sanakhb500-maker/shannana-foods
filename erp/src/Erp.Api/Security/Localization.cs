using System.Globalization;
using Erp.Application.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Erp.Api.Security;

/// <summary>
/// Reads the caller's language from the X-Lang header (the Angular app sends it on every call) and makes it the
/// request's UI culture, so FluentValidation's built-in messages come out in Arabic too.
/// </summary>
public sealed class LanguageMiddleware(RequestDelegate next)
{
    public const string Header = "X-Lang";
    private const string ItemKey = "lang";

    public static string Of(HttpContext ctx) => ctx.Items[ItemKey] as string ?? "en";

    public async Task InvokeAsync(HttpContext ctx)
    {
        var lang = ctx.Request.Headers[Header].ToString().StartsWith("ar", StringComparison.OrdinalIgnoreCase) ? "ar" : "en";
        ctx.Items[ItemKey] = lang;
        CultureInfo.CurrentUICulture = new CultureInfo(lang);
        await next(ctx);
    }
}

/// <summary>
/// The single place where failure messages are translated: every failed ApiResponse that leaves a controller (and every
/// error field text inside it) goes through the <see cref="MessageCatalog"/> for the caller's language.
/// </summary>
public sealed class LocalizeFailuresFilter : IAsyncResultFilter
{
    public Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.Result is ObjectResult { Value: ApiResponse<object> { Success: false } failure } result)
            result.Value = Localize(failure, LanguageMiddleware.Of(context.HttpContext));
        return next();
    }

    public static ApiResponse<object> Localize(ApiResponse<object> r, string lang) => new()
    {
        Success = false,
        Message = MessageCatalog.Localize(r.Message, lang),
        Errors = r.Errors?.ToDictionary(kv => kv.Key, kv => kv.Value.Select(m => MessageCatalog.Localize(m, lang)).ToArray()),
    };
}
