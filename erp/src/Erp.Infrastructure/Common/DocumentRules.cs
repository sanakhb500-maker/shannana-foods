using Erp.Application.Common;
using Erp.Domain.Entities;
using Erp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Erp.Infrastructure.Common;

/// <summary>Rules and helpers shared by every document-style use case (purchasing, sales, ...).</summary>
public static class DocumentRules
{
    /// <summary>Money rounding used everywhere: 4 decimals, halves away from zero (matches SQL ROUND).</summary>
    public static decimal R4(decimal v) => Math.Round(v, 4, MidpointRounding.AwayFromZero);

    public static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public static (int Page, int Size) Paging(int page, int size) => (Math.Max(1, page), Math.Clamp(size, 1, 200));

    public static void EnsureDraft(DocumentStatus s)
    {
        if (s != DocumentStatus.Draft) throw new BusinessRuleException("Only draft documents can be edited or confirmed.");
    }

    public static async Task EnsureItemsAsync(this AppDbContext db, IEnumerable<int> itemIds, CancellationToken ct)
    {
        var ids = itemIds.Distinct().ToList();
        if (await db.Items.CountAsync(i => ids.Contains(i.Id) && i.IsActive, ct) != ids.Count)
            throw new BusinessRuleException("An item does not exist or is inactive.");
    }

    public static async Task EnsureWarehouseAsync(this AppDbContext db, int warehouseId, CancellationToken ct)
    {
        if (!await db.Warehouses.AnyAsync(w => w.Id == warehouseId && w.IsActive, ct))
            throw new BusinessRuleException("The warehouse does not exist or is inactive.");
    }

    /// <summary>1 for the local currency; otherwise the provided rate, the fallback, or the latest stored rate on/before the date.</summary>
    public static async Task<decimal> ResolveRateAsync(this AppDbContext db, int currencyId, DateOnly date, decimal? provided, decimal? fallback, CancellationToken ct)
    {
        var cur = await db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == currencyId && c.IsActive, ct)
                  ?? throw new BusinessRuleException("The currency does not exist or is inactive.");
        if (cur.IsLocal) return 1m;
        if (provided is > 0) return provided.Value;
        if (fallback is > 0) return fallback.Value;

        var stored = await db.ExchangeRates.AsNoTracking()
            .Where(r => r.CurrencyId == currencyId && r.IsActive && r.Date <= date)
            .OrderByDescending(r => r.Date).Select(r => (decimal?)r.Rate).FirstOrDefaultAsync(ct);
        return stored ?? throw new BusinessRuleException($"No exchange rate for {cur.Code} on or before {date:yyyy-MM-dd}. Enter one.");
    }
}
