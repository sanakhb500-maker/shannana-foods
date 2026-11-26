using Erp.Domain.Common;
using Erp.Domain.Entities;
using Erp.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Erp.Infrastructure.Persistence;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider sp, CancellationToken ct = default)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var roleMgr = sp.GetRequiredService<RoleManager<AppRole>>();
        var userMgr = sp.GetRequiredService<UserManager<AppUser>>();
        var cfg = sp.GetRequiredService<IConfiguration>();
        var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("DbSeeder");

        foreach (var r in Roles.All)
            if (!await roleMgr.RoleExistsAsync(r))
                await roleMgr.CreateAsync(new AppRole(r));

        if (!await db.Currencies.AnyAsync(ct))
        {
            // The local currency is configurable; it can also be edited later from the UI.
            var localCode = (cfg["Seed:LocalCurrencyCode"] ?? "JOD").ToUpperInvariant();
            db.Currencies.AddRange(
                new Currency { Code = localCode, NameAr = cfg["Seed:LocalCurrencyNameAr"] ?? "العملة المحلية", NameEn = cfg["Seed:LocalCurrencyNameEn"] ?? "Local currency", Symbol = localCode, IsLocal = true },
                new Currency { Code = "USD", NameAr = "دولار أمريكي", NameEn = "US Dollar", Symbol = "$", IsLocal = false });
        }

        if (!await db.Units.AnyAsync(ct))
            db.Units.AddRange(
                new Unit { Code = "PCS", NameAr = "قطعة", NameEn = "Piece" },
                new Unit { Code = "CTN", NameAr = "كرتونة", NameEn = "Carton" },
                new Unit { Code = "KG", NameAr = "كيلوغرام", NameEn = "Kilogram" },
                new Unit { Code = "L", NameAr = "لتر", NameEn = "Liter" });

        if (!await db.Warehouses.AnyAsync(ct))
            db.Warehouses.Add(new Warehouse { Code = "MAIN", NameAr = "المستودع الرئيسي", NameEn = "Main warehouse" });

        await db.SaveChangesAsync(ct);

        if (!await userMgr.Users.AnyAsync(ct))
        {
            var email = cfg["Seed:AdminEmail"];
            var password = cfg["Seed:AdminPassword"];
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                log.LogWarning("No users exist and Seed:AdminEmail / Seed:AdminPassword are not set; skipping admin seed.");
                return;
            }
            var admin = new AppUser { UserName = email, Email = email, EmailConfirmed = true, FullName = cfg["Seed:AdminName"] ?? "Administrator" };
            var res = await userMgr.CreateAsync(admin, password);
            if (!res.Succeeded)
                throw new InvalidOperationException("Admin seed failed: " + string.Join("; ", res.Errors.Select(e => e.Description)));
            await userMgr.AddToRoleAsync(admin, Roles.Admin);
            log.LogInformation("Seeded admin user {Email}", email);
        }
    }
}
