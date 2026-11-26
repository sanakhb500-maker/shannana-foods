using Erp.Application.Common;
using Erp.Domain.Entities;
using Erp.Domain.Inventory;
using Erp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Erp.Infrastructure.Inventory;

/// <summary>Where a stock posting comes from; stamped onto every movement it creates.</summary>
public readonly record struct PostingSource(DateOnly Date, string DocumentType, int DocumentId, string? DocumentNumber);

/// <summary>
/// The ONLY place stock quantity and cost change. Every module (inventory, purchasing, later sales) posts through here.
/// It only stages changes on the shared scoped DbContext: the calling use case commits them with ONE SaveChanges
/// together with its own document changes, so a posting is atomic with the document that caused it.
/// </summary>
public sealed class StockPoster(AppDbContext db, ICurrentUser user, IConfiguration cfg)
{
    private bool AllowNegativeStock => cfg.GetValue<bool>("Inventory:AllowNegativeStock");
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public async Task<StockBalance> BalanceAsync(int itemId, int warehouseId, CancellationToken ct)
    {
        var tracked = db.StockBalances.Local.FirstOrDefault(b => b.ItemId == itemId && b.WarehouseId == warehouseId);
        if (tracked is not null) return tracked;

        var balance = await db.StockBalances.FirstOrDefaultAsync(b => b.ItemId == itemId && b.WarehouseId == warehouseId, ct);
        if (balance is null)
        {
            balance = new StockBalance { ItemId = itemId, WarehouseId = warehouseId };
            db.StockBalances.Add(balance);
        }
        return balance;
    }

    private void AddMovement(PostingSource s, int itemId, int warehouseId, decimal signedQty, decimal unitCost, MovementType type, int? reversalOf) =>
        db.StockMovements.Add(new StockMovement
        {
            Date = s.Date, ItemId = itemId, WarehouseId = warehouseId, Quantity = signedQty, UnitCost = unitCost, Type = type,
            DocumentType = s.DocumentType, DocumentId = s.DocumentId, DocumentNumber = s.DocumentNumber,
            ReversalOfId = reversalOf, CreatedAt = DateTime.UtcNow, CreatedBy = user.UserName,
        });

    /// <summary>Adds stock at <paramref name="unitCost"/> (local currency) and re-averages the cost.</summary>
    public async Task ReceiveAsync(PostingSource s, int itemId, int warehouseId, decimal qty, decimal unitCost, MovementType type, int? reversalOf, CancellationToken ct)
    {
        var b = await BalanceAsync(itemId, warehouseId, ct);
        b.AverageCost = Costing.MovingAverage(b.Quantity, b.AverageCost, qty, unitCost);
        b.Quantity += qty;
        b.LastMovementAt = DateTime.UtcNow;
        AddMovement(s, itemId, warehouseId, qty, unitCost, type, reversalOf);
    }

    /// <summary>Takes stock out at the current average cost and returns that cost.</summary>
    public async Task<decimal> IssueAsync(PostingSource s, int itemId, int warehouseId, decimal qty, MovementType type, int? reversalOf, CancellationToken ct)
    {
        var b = await BalanceAsync(itemId, warehouseId, ct);
        if (!AllowNegativeStock && b.Quantity < qty)
        {
            var item = await db.Items.Where(i => i.Id == itemId).Select(i => i.Code).FirstOrDefaultAsync(ct);
            var wh = await db.Warehouses.Where(w => w.Id == warehouseId).Select(w => w.Code).FirstOrDefaultAsync(ct);
            throw new BusinessRuleException(
                $"Insufficient stock for item {item} in warehouse {wh}: available {b.Quantity:0.####}, required {qty:0.####}.");
        }
        var cost = b.AverageCost;
        b.Quantity -= qty;
        b.LastMovementAt = DateTime.UtcNow;
        AddMovement(s, itemId, warehouseId, -qty, cost, type, reversalOf);
        return cost;
    }

    /// <summary>Undo every movement of a confirmed document: stock that came in goes back out, stock that went out comes back at its original cost.</summary>
    public async Task ReverseDocumentAsync(string documentType, int documentId, string? number, CancellationToken ct)
    {
        var source = new PostingSource(Today, documentType, documentId, number);
        var originals = await db.StockMovements
            .Where(m => m.DocumentType == documentType && m.DocumentId == documentId && m.ReversalOfId == null)
            .OrderByDescending(m => m.Id).ToListAsync(ct);

        foreach (var m in originals)
        {
            if (m.Quantity > 0)
            {
                try { await IssueAsync(source, m.ItemId, m.WarehouseId, m.Quantity, MovementType.Reversal, m.Id, ct); }
                catch (BusinessRuleException ex)
                {
                    throw new BusinessRuleException("This document cannot be cancelled because its stock has already been used. " + ex.Message);
                }
            }
            else
            {
                await ReceiveAsync(source, m.ItemId, m.WarehouseId, -m.Quantity, m.UnitCost, MovementType.Reversal, m.Id, ct);
            }
        }
    }
}
