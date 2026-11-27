using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Erp.Api.Security;
using Erp.Application.Accounting;
using Erp.Application.Common;
using Erp.Application.DriverApp;
using Erp.Application.Expenses;
using Erp.Application.Validators;
using Erp.Domain.Common;
using Erp.Application.Inventory;
using Erp.Application.Purchasing;
using Erp.Application.Sales;
using Erp.Infrastructure.Accounting;
using Erp.Infrastructure.Auth;
using Erp.Infrastructure.Common;
using Erp.Infrastructure.DriverApp;
using Erp.Infrastructure.Inventory;
using Erp.Infrastructure.Identity;
using Erp.Infrastructure.Expenses;
using Erp.Infrastructure.Persistence;
using Erp.Infrastructure.Purchasing;
using Erp.Infrastructure.Sales;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;

// ---------- configuration checks (fail fast) ----------
var jwtOptions = cfg.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
if (jwtOptions.Key.Length < 32)
    throw new InvalidOperationException("Jwt:Key must be set to a random secret of at least 32 characters.");
var connectionString = cfg.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

// ---------- services ----------
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<StockPoster>();
builder.Services.AddScoped<DocumentNumbering>();
builder.Services.AddScoped<JournalPoster>();
builder.Services.AddScoped<IAccountingService, AccountingService>();
builder.Services.AddScoped<IRevaluationService, RevaluationService>();
builder.Services.AddScoped<IExpenseService, ExpenseService>();
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<IPurchasingService, PurchasingService>();
builder.Services.AddScoped<ISalesService, SalesService>();
builder.Services.Configure<StorageOptions>(cfg.GetSection(StorageOptions.Section));
builder.Services.PostConfigure<StorageOptions>(o => o.ProofsPath = Path.GetFullPath(o.ProofsPath, builder.Environment.ContentRootPath));
builder.Services.AddScoped<IDriverService, DriverService>();
// The browser preview of the Flutter app (dev only) runs on another localhost port.
builder.Services.AddCors(o => o.AddPolicy("dev", p => p.SetIsOriginAllowed(origin => Uri.TryCreate(origin, UriKind.Absolute, out var u) && u.IsLoopback).AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlServer(connectionString));

builder.Services.AddIdentityCore<AppUser>(o =>
    {
        o.Password.RequiredLength = 8;
        o.Password.RequireNonAlphanumeric = false;
        o.User.RequireUniqueEmail = true;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddRoles<AppRole>()
    .AddEntityFrameworkStores<AppDbContext>();

builder.Services.Configure<JwtOptions>(cfg.GetSection(JwtOptions.Section));
builder.Services.AddSingleton<ITokenService, TokenService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true, ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
        };
        o.Events = new JwtBearerEvents
        {
            // The token travels in an HttpOnly cookie, never in JS-accessible storage.
            OnMessageReceived = ctx =>
            {
                ctx.Token = ctx.Request.Cookies[AuthCookies.Auth];
                return Task.CompletedTask;
            },
            // A deactivated user loses access immediately, not when the token expires.
            OnTokenValidated = async ctx =>
            {
                var um = ctx.HttpContext.RequestServices.GetRequiredService<UserManager<AppUser>>();
                var id = ctx.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                var user = id is null ? null : await um.FindByIdAsync(id);
                if (user is null || !user.IsActive) ctx.Fail("User is inactive.");
            },
        };
    });

builder.Services.AddAuthorization(o =>
{
    foreach (var (policy, roles) in Policies.Map)
        o.AddPolicy(policy, p => p.RequireAuthenticatedUser().RequireRole(roles));
});

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = cfg.GetValue("RateLimit:LoginPermitLimit", 10), Window = TimeSpan.FromMinutes(15), QueueLimit = 0 }));
});

builder.Services.AddValidatorsFromAssemblyContaining<CurrencyValidator>();
builder.Services.AddOpenApi();

FluentValidation.ValidatorOptions.Global.DisplayNameResolver = (_, member, _) => FieldNames.For(member?.Name);

builder.Services.AddControllers(o => o.Filters.Add<LocalizeFailuresFilter>())
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    .ConfigureApiBehaviorOptions(o =>
        o.InvalidModelStateResponseFactory = ctx =>
        {
            var errors = ctx.ModelState.Where(kv => kv.Value?.Errors.Count > 0)
                .ToDictionary(kv => kv.Key, kv => kv.Value!.Errors.Select(e => e.ErrorMessage).ToArray());
            return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(ApiResponse.Fail("Validation failed.", errors));
        });

var app = builder.Build();

// ---------- pipeline ----------
app.UseMiddleware<LanguageMiddleware>();
app.UseExceptionHandler(errApp => errApp.Run(async ctx =>
{
    var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
    // Expected, business-level failures keep their message; everything else is a generic 500.
    var (status, message) = ex switch
    {
        BusinessRuleException b => (StatusCodes.Status400BadRequest, b.Message),
        NotFoundException n => (StatusCodes.Status404NotFound, n.Message),
        ConcurrencyConflictException c => (StatusCodes.Status409Conflict, c.Message),
        _ => (StatusCodes.Status500InternalServerError, "Unexpected server error."),
    };
    if (status == StatusCodes.Status500InternalServerError)
        app.Logger.LogError(ex, "Unhandled exception for {Path}", ctx.Request.Path);
    ctx.Response.StatusCode = status;
    await ctx.Response.WriteAsJsonAsync(ApiResponse.Fail(MessageCatalog.Localize(message, LanguageMiddleware.Of(ctx))));
}));

if (!app.Environment.IsDevelopment()) app.UseHsts(); // browsers remember to use https only
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();
if (app.Environment.IsDevelopment()) app.UseCors("dev");
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<DriverScopeMiddleware>();
app.UseMiddleware<CsrfMiddleware>();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.MapControllers();
app.MapGet("/api/v1/health", () => Results.Ok(ApiResponse.Ok(new { status = "ok" }))).AllowAnonymous();
app.MapFallbackToFile("index.html"); // Angular SPA (built into wwwroot)

// ---------- database ----------
if (app.Environment.IsDevelopment() || cfg.GetValue<bool>("Database:AutoMigrate"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (db.Database.IsRelational()) await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(scope.ServiceProvider);
}

app.Run();

public partial class Program; // for WebApplicationFactory in tests
