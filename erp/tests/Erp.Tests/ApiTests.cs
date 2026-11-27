using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Erp.Tests;

public class ErpFactory : WebApplicationFactory<Program>
{
    private readonly string _db = "erp-tests-" + Guid.NewGuid();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development"); // brings dev Jwt key + seeded admin from appsettings.Development.json
        builder.UseSetting("Storage:ProofsPath", Path.Combine(Path.GetTempPath(), "erp-proofs-" + Guid.NewGuid().ToString("N"))); // proof files of the driver tests
        builder.UseSetting("RateLimit:LoginPermitLimit", "10000"); // every test logs in; the limiter itself is exercised in production only
        builder.ConfigureServices(s =>
        {
            s.RemoveAll<DbContextOptions<AppDbContext>>();
            s.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            s.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_db));
        });
    }
}

public class ApiTests(ErpFactory factory) : IClassFixture<ErpFactory>
{
    private const string AdminEmail = "admin@shannana.local";
    private const string AdminPassword = "Admin#12345";

    private sealed record Session(HttpClient Client, string Csrf);

    private async Task<Session> LoginAsync(string email, string password)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var res = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        return new Session(client, json.GetProperty("data").GetProperty("csrfToken").GetString()!);
    }

    private static HttpRequestMessage Json(HttpMethod method, string url, object? body, string? csrf)
    {
        var req = new HttpRequestMessage(method, url);
        if (body is not null) req.Content = JsonContent.Create(body);
        if (csrf is not null) req.Headers.Add("X-CSRF-Token", csrf);
        return req;
    }

    private async Task<Session> NewUserSessionAsync(string email, string role)
    {
        var admin = await LoginAsync(AdminEmail, AdminPassword);
        var res = await admin.Client.SendAsync(Json(HttpMethod.Post, "/api/v1/users",
            new { email, fullName = role, password = "Passw0rd!x", roles = new[] { role } }, admin.Csrf));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return await LoginAsync(email, "Passw0rd!x");
    }

    [Fact]
    public async Task Login_with_wrong_password_is_rejected()
    {
        var client = factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = AdminEmail, password = "nope" });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Protected_endpoint_requires_login()
    {
        var res = await factory.CreateClient().GetAsync("/api/v1/items");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Login_then_me_returns_roles_and_logout_clears_session()
    {
        var s = await LoginAsync(AdminEmail, AdminPassword);
        var me = await s.Client.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");
        Assert.Equal("Admin", me.GetProperty("data").GetProperty("roles")[0].GetString());

        var logout = await s.Client.SendAsync(Json(HttpMethod.Post, "/api/v1/auth/logout", null, s.Csrf));
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Client.GetAsync("/api/v1/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Write_without_csrf_token_is_forbidden()
    {
        var s = await LoginAsync(AdminEmail, AdminPassword);
        var res = await s.Client.SendAsync(Json(HttpMethod.Post, "/api/v1/units",
            new { code = "BX", nameAr = "علبة", nameEn = "Box" }, csrf: null));
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task StoreKeeper_cannot_manage_users_or_suppliers_but_can_manage_items()
    {
        var sk = await NewUserSessionAsync("sk1@test.local", "StoreKeeper");

        Assert.Equal(HttpStatusCode.Forbidden, (await sk.Client.GetAsync("/api/v1/users")).StatusCode);

        var supplier = await sk.Client.SendAsync(Json(HttpMethod.Post, "/api/v1/suppliers",
            new { code = "S-1", nameAr = "مورد", nameEn = "Supplier" }, sk.Csrf));
        Assert.Equal(HttpStatusCode.Forbidden, supplier.StatusCode);

        var item = await sk.Client.SendAsync(Json(HttpMethod.Post, "/api/v1/items",
            new { code = "SK-ITEM", nameAr = "صنف", nameEn = "Item", unitId = 1 }, sk.Csrf));
        Assert.Equal(HttpStatusCode.Created, item.StatusCode);

        // everyone signed in can read
        Assert.Equal(HttpStatusCode.OK, (await sk.Client.GetAsync("/api/v1/suppliers")).StatusCode);
    }

    [Fact]
    public async Task Invalid_item_returns_400_with_field_errors()
    {
        var s = await LoginAsync(AdminEmail, AdminPassword);
        var res = await s.Client.SendAsync(Json(HttpMethod.Post, "/api/v1/items",
            new { code = "bad code", nameAr = "", nameEn = "x", unitId = 0 }, s.Csrf));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.True(json.GetProperty("errors").TryGetProperty("Code", out _));
    }

    [Fact]
    public async Task Item_codes_are_normalised_and_delete_is_a_soft_deactivate()
    {
        var s = await LoginAsync(AdminEmail, AdminPassword);
        var create = await s.Client.SendAsync(Json(HttpMethod.Post, "/api/v1/items",
            new { code = "ket-9", nameAr = "كاتشب", nameEn = "Ketchup", unitId = 1, costPrice = 1.25, salePrice = 2 }, s.Csrf));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.Equal("KET-9", created.GetProperty("code").GetString());
        var id = created.GetProperty("id").GetInt32();

        var del = await s.Client.SendAsync(Json(HttpMethod.Delete, $"/api/v1/items/{id}", null, s.Csrf));
        Assert.Equal(HttpStatusCode.OK, del.StatusCode);

        var after = (await s.Client.GetFromJsonAsync<JsonElement>($"/api/v1/items/{id}")).GetProperty("data");
        Assert.False(after.GetProperty("isActive").GetBoolean());

        var activeOnly = await s.Client.GetFromJsonAsync<JsonElement>("/api/v1/items?activeOnly=true&q=KET-9");
        Assert.Equal(0, activeOnly.GetProperty("data").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Cannot_create_a_second_local_currency_or_a_rate_for_the_local_currency()
    {
        var s = await LoginAsync(AdminEmail, AdminPassword);

        var second = await s.Client.SendAsync(Json(HttpMethod.Post, "/api/v1/currencies",
            new { code = "EUR", nameAr = "يورو", nameEn = "Euro", symbol = "E", isLocal = true }, s.Csrf));
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);

        // currency 1 is the seeded local currency
        var localRate = await s.Client.SendAsync(Json(HttpMethod.Post, "/api/v1/exchangerates",
            new { currencyId = 1, date = "2026-01-01", rate = 1 }, s.Csrf));
        Assert.Equal(HttpStatusCode.BadRequest, localRate.StatusCode);

        // USD (id 2) accepts a rate; rate must be positive
        var usd = await s.Client.SendAsync(Json(HttpMethod.Post, "/api/v1/exchangerates",
            new { currencyId = 2, date = "2026-01-01", rate = 0.71 }, s.Csrf));
        Assert.Equal(HttpStatusCode.Created, usd.StatusCode);
        var zero = await s.Client.SendAsync(Json(HttpMethod.Post, "/api/v1/exchangerates",
            new { currencyId = 2, date = "2026-01-02", rate = 0 }, s.Csrf));
        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);
    }

    [Fact]
    public async Task Admin_cannot_remove_own_admin_role()
    {
        var s = await LoginAsync(AdminEmail, AdminPassword);
        var users = (await s.Client.GetFromJsonAsync<JsonElement>("/api/v1/users")).GetProperty("data");
        var me = users.EnumerateArray().First(u => u.GetProperty("email").GetString() == AdminEmail);
        var res = await s.Client.SendAsync(Json(HttpMethod.Put, $"/api/v1/users/{me.GetProperty("id").GetInt32()}",
            new { fullName = "Administrator", isActive = true, roles = new[] { "Sales" } }, s.Csrf));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
