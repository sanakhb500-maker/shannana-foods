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
    /// <summary>
    /// A small starter chart. The accounts carrying a SystemRole are the ones automatic postings use; they can be
    /// renamed or re-coded freely, but each role must keep exactly one active account.
    /// </summary>
    private static async Task SeedChartOfAccountsAsync(AppDbContext db, CancellationToken ct)
    {
        if (!await db.Accounts.AnyAsync(ct))
        {
            (string Code, string Ar, string En, AccountType Type)[] headers =
            [
                ("1000", "الأصول", "Assets", AccountType.Asset),
                ("2000", "الالتزامات", "Liabilities", AccountType.Liability),
                ("3000", "حقوق الملكية", "Equity", AccountType.Equity),
                ("4000", "الإيرادات", "Revenue", AccountType.Revenue),
                ("5000", "المصروفات", "Expenses", AccountType.Expense),
            ];
            var parents = new Dictionary<AccountType, Account>();
            foreach (var h in headers)
            {
                var a = new Account { Code = h.Code, NameAr = h.Ar, NameEn = h.En, Type = h.Type, IsPostable = false };
                parents[h.Type] = a;
                db.Accounts.Add(a);
            }
            await db.SaveChangesAsync(ct); // parents need ids

            (string Code, string Ar, string En, AccountType Type, string Role)[] leaves =
            [
                ("1100", "الصندوق", "Cash", AccountType.Asset, AccountRoles.Cash),
                ("1200", "البنك", "Bank", AccountType.Asset, AccountRoles.Bank),
                ("1300", "الذمم المدينة (العملاء)", "Accounts receivable", AccountType.Asset, AccountRoles.Receivables),
                ("1400", "المخزون", "Inventory", AccountType.Asset, AccountRoles.Inventory),
                ("2100", "الذمم الدائنة (الموردون)", "Accounts payable", AccountType.Liability, AccountRoles.Payables),
                ("2200", "بضاعة مستلمة غير مفوترة", "Goods received not invoiced", AccountType.Liability, AccountRoles.GoodsReceivedNotInvoiced),
                ("3100", "رصيد افتتاحي - حقوق الملكية", "Opening balance equity", AccountType.Equity, AccountRoles.OpeningEquity),
                ("4100", "إيرادات المبيعات", "Sales revenue", AccountType.Revenue, AccountRoles.SalesRevenue),
                ("4200", "مردودات ومسموحات المبيعات", "Sales returns and allowances", AccountType.Revenue, AccountRoles.SalesReturns),
                ("5100", "تكلفة البضاعة المباعة", "Cost of goods sold", AccountType.Expense, AccountRoles.CostOfGoodsSold),
                ("5200", "فروقات جرد المخزون", "Inventory adjustments", AccountType.Expense, AccountRoles.InventoryAdjustments),
            ];
            foreach (var l in leaves)
                db.Accounts.Add(new Account
                {
                    Code = l.Code, NameAr = l.Ar, NameEn = l.En, Type = l.Type, ParentId = parents[l.Type].Id, IsPostable = true, SystemRole = l.Role,
                });
        }

        if (!await db.AccountingSettings.AnyAsync(ct)) db.AccountingSettings.Add(new AccountingSetting());
        await db.SaveChangesAsync(ct);
    }

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
            var localCode = (cfg["Seed:LocalCurrencyCode"] ?? "SYP").ToUpperInvariant();
            var isSyp = localCode == "SYP";
            db.Currencies.AddRange(
                new Currency { Code = localCode, NameAr = cfg["Seed:LocalCurrencyNameAr"] ?? (isSyp ? "الليرة السورية الجديدة" : "العملة المحلية"),
                    NameEn = cfg["Seed:LocalCurrencyNameEn"] ?? (isSyp ? "New Syrian pound" : "Local currency"),
                    Symbol = isSyp ? "ل.س" : localCode, IsLocal = true },
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

        await SeedChartOfAccountsAsync(db, ct);

        foreach (var key in SequenceKeys.All)
            if (!await db.DocumentSequences.AnyAsync(x => x.Key == key, ct))
                db.DocumentSequences.Add(new DocumentSequence { Key = key });
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
