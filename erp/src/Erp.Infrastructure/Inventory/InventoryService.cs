using Erp.Application.Common;
using Erp.Application.Inventory;
using Erp.Domain.Entities;
using Erp.Domain.Inventory;
using Erp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Erp.Infrastructure.Inventory;

public class InventoryService(AppDbContext db, ICurrentUser user, IConfiguration cfg) : IInventoryService
{
    /// <summary>Where a posting comes from; stamped onto every movement it creates.</summary>
    private readonly record struct Source(DateOnly Date, string DocumentType, int DocumentId, string? DocumentNumber);

    private bool AllowNegativeStock => cfg.GetValue<bool>("Inventory:AllowNegativeStock");
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    // =====================================================================
    //  Core posting: every stock change goes through Receive / Issue.
    //  Movement + balance changes are only staged here; the caller commits
    //  them with ONE SaveChanges together with the document status.
    // =====================================================================

    private async Task<StockBalance> BalanceAsync(int itemId, int warehouseId, CancellationToken ct)
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

    private void AddMovement(Source s, int itemId, int warehouseId, decimal signedQty, decimal unitCost, MovementType type, int? reversalOf) =>
        db.StockMovements.Add(new StockMovement
        {
            Date = s.Date, ItemId = itemId, WarehouseId = warehouseId, Quantity = signedQty, UnitCost = unitCost, Type = type,
            DocumentType = s.DocumentType, DocumentId = s.DocumentId, DocumentNumber = s.DocumentNumber,
            ReversalOfId = reversalOf, CreatedAt = DateTime.UtcNow, CreatedBy = user.UserName,
        });

    private async Task ReceiveAsync(Source s, int itemId, int warehouseId, decimal qty, decimal unitCost, MovementType type, int? reversalOf, CancellationToken ct)
    {
        var b = await BalanceAsync(itemId, warehouseId, ct);
        b.AverageCost = Costing.MovingAverage(b.Quantity, b.AverageCost, qty, unitCost);
        b.Quantity += qty;
        b.LastMovementAt = DateTime.UtcNow;
        AddMovement(s, itemId, warehouseId, qty, unitCost, type, reversalOf);
    }

    /// <summary>Takes stock out at the current average cost and returns that cost.</summary>
    private async Task<decimal> IssueAsync(Source s, int itemId, int warehouseId, decimal qty, MovementType type, int? reversalOf, CancellationToken ct)
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
    private async Task ReverseDocumentAsync(string documentType, int documentId, string? number, CancellationToken ct)
    {
        var source = new Source(Today, documentType, documentId, number);
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

    private async Task<int> SaveAsync(CancellationToken ct)
    {
        try { return await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException();
        }
    }

    private async Task EnsureActiveAsync(IEnumerable<int> warehouseIds, IEnumerable<int> itemIds, CancellationToken ct)
    {
        var wh = warehouseIds.Distinct().ToList();
        var okWh = await db.Warehouses.CountAsync(w => wh.Contains(w.Id) && w.IsActive, ct);
        if (okWh != wh.Count) throw new BusinessRuleException("A warehouse does not exist or is inactive.");

        var items = itemIds.Distinct().ToList();
        var okItems = await db.Items.CountAsync(i => items.Contains(i.Id) && i.IsActive, ct);
        if (okItems != items.Count) throw new BusinessRuleException("An item does not exist or is inactive.");
    }

    // =====================================================================
    //  Transfers
    // =====================================================================

    public async Task<PagedResult<TransferRow>> ListTransfersAsync(DocumentFilter f, CancellationToken ct)
    {
        var q = db.StockTransfers.AsNoTracking().AsQueryable();
        if (f.Status is { } st) q = q.Where(t => t.Status == st);
        if (f.WarehouseId is { } w) q = q.Where(t => t.FromWarehouseId == w || t.ToWarehouseId == w);
        if (!string.IsNullOrWhiteSpace(f.Q)) { var s = f.Q.Trim(); q = q.Where(t => (t.Number != null && t.Number.Contains(s)) || (t.Notes != null && t.Notes.Contains(s))); }

        var (page, size) = Paging(f.Page, f.PageSize);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(t => t.Date).ThenByDescending(t => t.Id).Skip((page - 1) * size).Take(size)
            .Select(t => new TransferRow(t.Id, t.Number, t.Date, t.FromWarehouseId, t.ToWarehouseId, t.Status, t.Notes, t.Lines.Count, t.CreatedAt, t.CreatedBy))
            .ToListAsync(ct);
        return new PagedResult<TransferRow>(rows, total, page, size);
    }

    public async Task<StockTransfer> GetTransferAsync(int id, CancellationToken ct) =>
        await db.StockTransfers.AsNoTracking().Include(t => t.Lines).FirstOrDefaultAsync(t => t.Id == id, ct)
        ?? throw new NotFoundException("Transfer not found.");

    public async Task<StockTransfer> CreateTransferAsync(TransferInput i, CancellationToken ct)
    {
        await EnsureActiveAsync([i.FromWarehouseId, i.ToWarehouseId], i.Lines.Select(l => l.ItemId), ct);
        var t = new StockTransfer
        {
            Date = i.Date, FromWarehouseId = i.FromWarehouseId, ToWarehouseId = i.ToWarehouseId,
            Notes = Clean(i.Notes), Status = DocumentStatus.Draft,
            Lines = i.Lines.Select(l => new StockTransferLine { ItemId = l.ItemId, Quantity = l.Quantity }).ToList(),
        };
        db.StockTransfers.Add(t);
        await SaveAsync(ct);
        t.Number = $"TRF-{t.Id:D6}"; // id-based number: unique and race-free
        await SaveAsync(ct);
        return t;
    }

    public async Task<StockTransfer> UpdateTransferAsync(int id, TransferInput i, CancellationToken ct)
    {
        var t = await LoadTransferAsync(id, ct);
        EnsureDraft(t.Status);
        await EnsureActiveAsync([i.FromWarehouseId, i.ToWarehouseId], i.Lines.Select(l => l.ItemId), ct);

        t.Date = i.Date; t.FromWarehouseId = i.FromWarehouseId; t.ToWarehouseId = i.ToWarehouseId; t.Notes = Clean(i.Notes);
        db.StockTransferLines.RemoveRange(t.Lines);
        t.Lines = i.Lines.Select(l => new StockTransferLine { ItemId = l.ItemId, Quantity = l.Quantity }).ToList();
        await SaveAsync(ct);
        return t;
    }

    public async Task<StockTransfer> ConfirmTransferAsync(int id, CancellationToken ct)
    {
        var t = await LoadTransferAsync(id, ct);
        EnsureDraft(t.Status);
        await EnsureActiveAsync([t.FromWarehouseId, t.ToWarehouseId], t.Lines.Select(l => l.ItemId), ct);

        var src = new Source(t.Date, DocumentTypes.Transfer, t.Id, t.Number);
        foreach (var line in t.Lines)
        {
            var cost = await IssueAsync(src, line.ItemId, t.FromWarehouseId, line.Quantity, MovementType.TransferOut, null, ct);
            await ReceiveAsync(src, line.ItemId, t.ToWarehouseId, line.Quantity, cost, MovementType.TransferIn, null, ct);
        }
        t.Status = DocumentStatus.Confirmed;
        t.ConfirmedAt = DateTime.UtcNow;
        t.ConfirmedBy = user.UserName;
        await SaveAsync(ct);
        return t;
    }

    public async Task<StockTransfer> CancelTransferAsync(int id, CancellationToken ct)
    {
        var t = await LoadTransferAsync(id, ct);
        if (t.Status == DocumentStatus.Cancelled) throw new BusinessRuleException("The document is already cancelled.");
        if (t.Status == DocumentStatus.Confirmed) await ReverseDocumentAsync(DocumentTypes.Transfer, t.Id, t.Number, ct);
        t.Status = DocumentStatus.Cancelled;
        await SaveAsync(ct);
        return t;
    }

    private async Task<StockTransfer> LoadTransferAsync(int id, CancellationToken ct) =>
        await db.StockTransfers.Include(t => t.Lines).FirstOrDefaultAsync(t => t.Id == id, ct)
        ?? throw new NotFoundException("Transfer not found.");

    // =====================================================================
    //  Adjustments (stock count / opening balance / write-off)
    // =====================================================================

    public async Task<PagedResult<AdjustmentRow>> ListAdjustmentsAsync(DocumentFilter f, CancellationToken ct)
    {
        var q = db.StockAdjustments.AsNoTracking().AsQueryable();
        if (f.Status is { } st) q = q.Where(a => a.Status == st);
        if (f.WarehouseId is { } w) q = q.Where(a => a.WarehouseId == w);
        if (!string.IsNullOrWhiteSpace(f.Q)) { var s = f.Q.Trim(); q = q.Where(a => (a.Number != null && a.Number.Contains(s)) || (a.Notes != null && a.Notes.Contains(s))); }

        var (page, size) = Paging(f.Page, f.PageSize);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(a => a.Date).ThenByDescending(a => a.Id).Skip((page - 1) * size).Take(size)
            .Select(a => new AdjustmentRow(a.Id, a.Number, a.Date, a.WarehouseId, a.Reason, a.Status, a.Notes, a.Lines.Count, a.CreatedAt, a.CreatedBy))
            .ToListAsync(ct);
        return new PagedResult<AdjustmentRow>(rows, total, page, size);
    }

    public async Task<StockAdjustment> GetAdjustmentAsync(int id, CancellationToken ct) =>
        await db.StockAdjustments.AsNoTracking().Include(a => a.Lines).FirstOrDefaultAsync(a => a.Id == id, ct)
        ?? throw new NotFoundException("Adjustment not found.");

    public async Task<StockAdjustment> CreateAdjustmentAsync(AdjustmentInput i, CancellationToken ct)
    {
        await EnsureActiveAsync([i.WarehouseId], i.Lines.Select(l => l.ItemId), ct);
        var a = new StockAdjustment
        {
            Date = i.Date, WarehouseId = i.WarehouseId, Reason = i.Reason, Notes = Clean(i.Notes), Status = DocumentStatus.Draft,
            Lines = i.Lines.Select(l => new StockAdjustmentLine { ItemId = l.ItemId, CountedQuantity = l.CountedQuantity, UnitCost = l.UnitCost }).ToList(),
        };
        db.StockAdjustments.Add(a);
        await SaveAsync(ct);
        a.Number = $"ADJ-{a.Id:D6}";
        await SaveAsync(ct);
        return a;
    }

    public async Task<StockAdjustment> UpdateAdjustmentAsync(int id, AdjustmentInput i, CancellationToken ct)
    {
        var a = await LoadAdjustmentAsync(id, ct);
        EnsureDraft(a.Status);
        await EnsureActiveAsync([i.WarehouseId], i.Lines.Select(l => l.ItemId), ct);

        a.Date = i.Date; a.WarehouseId = i.WarehouseId; a.Reason = i.Reason; a.Notes = Clean(i.Notes);
        db.StockAdjustmentLines.RemoveRange(a.Lines);
        a.Lines = i.Lines.Select(l => new StockAdjustmentLine { ItemId = l.ItemId, CountedQuantity = l.CountedQuantity, UnitCost = l.UnitCost }).ToList();
        await SaveAsync(ct);
        return a;
    }

    public async Task<StockAdjustment> ConfirmAdjustmentAsync(int id, CancellationToken ct)
    {
        var a = await LoadAdjustmentAsync(id, ct);
        EnsureDraft(a.Status);
        await EnsureActiveAsync([a.WarehouseId], a.Lines.Select(l => l.ItemId), ct);

        var src = new Source(a.Date, DocumentTypes.Adjustment, a.Id, a.Number);
        foreach (var line in a.Lines)
        {
            var balance = await BalanceAsync(line.ItemId, a.WarehouseId, ct);
            var diff = line.CountedQuantity - balance.Quantity;
            line.SystemQuantity = balance.Quantity;
            line.Difference = diff;

            if (diff > 0)
            {
                // surplus enters at the stated cost, else the current average, else the item's standard cost
                var cost = line.UnitCost
                           ?? (balance.AverageCost > 0 ? balance.AverageCost
                               : await db.Items.Where(i => i.Id == line.ItemId).Select(i => i.CostPrice).FirstAsync(ct));
                await ReceiveAsync(src, line.ItemId, a.WarehouseId, diff, cost, MovementType.AdjustmentIn, null, ct);
            }
            else if (diff < 0)
            {
                await IssueAsync(src, line.ItemId, a.WarehouseId, -diff, MovementType.AdjustmentOut, null, ct);
            }
        }
        a.Status = DocumentStatus.Confirmed;
        a.ConfirmedAt = DateTime.UtcNow;
        a.ConfirmedBy = user.UserName;
        await SaveAsync(ct);
        return a;
    }

    public async Task<StockAdjustment> CancelAdjustmentAsync(int id, CancellationToken ct)
    {
        var a = await LoadAdjustmentAsync(id, ct);
        if (a.Status == DocumentStatus.Cancelled) throw new BusinessRuleException("The document is already cancelled.");
        if (a.Status == DocumentStatus.Confirmed) await ReverseDocumentAsync(DocumentTypes.Adjustment, a.Id, a.Number, ct);
        a.Status = DocumentStatus.Cancelled;
        await SaveAsync(ct);
        return a;
    }

    private async Task<StockAdjustment> LoadAdjustmentAsync(int id, CancellationToken ct) =>
        await db.StockAdjustments.Include(a => a.Lines).FirstOrDefaultAsync(a => a.Id == id, ct)
        ?? throw new NotFoundException("Adjustment not found.");

    // =====================================================================
    //  Reports
    // =====================================================================

    public async Task<BalancesResult> GetBalancesAsync(BalanceFilter f, CancellationToken ct)
    {
        var q = from b in db.StockBalances.AsNoTracking()
                join i in db.Items on b.ItemId equals i.Id
                join w in db.Warehouses on b.WarehouseId equals w.Id
                select new { b, i, w };

        if (f.WarehouseId is { } wid) q = q.Where(x => x.b.WarehouseId == wid);
        if (f.ItemId is { } iid) q = q.Where(x => x.b.ItemId == iid);
        if (f.NonZeroOnly) q = q.Where(x => x.b.Quantity != 0);
        if (!string.IsNullOrWhiteSpace(f.Q))
        {
            var s = f.Q.Trim();
            q = q.Where(x => x.i.Code.Contains(s) || x.i.NameAr.Contains(s) || x.i.NameEn.Contains(s));
        }

        var (page, size) = Paging(f.Page, f.PageSize, 500);
        var total = await q.CountAsync(ct);
        var totalValue = await q.SumAsync(x => (decimal?)(x.b.Quantity * x.b.AverageCost), ct) ?? 0m;
        var rows = await q.OrderBy(x => x.i.Code).ThenBy(x => x.w.Code).Skip((page - 1) * size).Take(size)
            .Select(x => new BalanceRow(x.i.Id, x.i.Code, x.i.NameAr, x.i.NameEn, x.w.Id, x.w.Code, x.w.NameAr, x.w.NameEn,
                x.b.Quantity, x.b.AverageCost, x.b.Quantity * x.b.AverageCost))
            .ToListAsync(ct);
        return new BalancesResult(rows, total, page, size, totalValue);
    }

    public async Task<PagedResult<MovementRow>> GetMovementsAsync(MovementFilter f, CancellationToken ct)
    {
        var q = from m in db.StockMovements.AsNoTracking()
                join i in db.Items on m.ItemId equals i.Id
                join w in db.Warehouses on m.WarehouseId equals w.Id
                select new { m, i, w };

        if (f.WarehouseId is { } wid) q = q.Where(x => x.m.WarehouseId == wid);
        if (f.ItemId is { } iid) q = q.Where(x => x.m.ItemId == iid);
        if (f.From is { } from) q = q.Where(x => x.m.Date >= from);
        if (f.To is { } to) q = q.Where(x => x.m.Date <= to);
        if (!string.IsNullOrWhiteSpace(f.DocumentType)) { var dt = f.DocumentType.Trim(); q = q.Where(x => x.m.DocumentType == dt); }

        var (page, size) = Paging(f.Page, f.PageSize, 500);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(x => x.m.Date).ThenByDescending(x => x.m.Id).Skip((page - 1) * size).Take(size)
            .Select(x => new MovementRow(x.m.Id, x.m.Date, x.i.Id, x.i.Code, x.i.NameAr, x.i.NameEn, x.w.Id, x.w.Code, x.w.NameAr, x.w.NameEn,
                x.m.Quantity, x.m.UnitCost, x.m.Quantity * x.m.UnitCost, x.m.Type, x.m.DocumentType, x.m.DocumentId, x.m.DocumentNumber, x.m.CreatedAt, x.m.CreatedBy))
            .ToListAsync(ct);
        return new PagedResult<MovementRow>(rows, total, page, size);
    }

    public async Task<IReadOnlyList<LowStockRow>> GetLowStockAsync(CancellationToken ct) =>
        await db.Items.AsNoTracking()
            .Where(i => i.IsActive && i.ReorderLevel > 0)
            .Select(i => new { i, Qty = db.StockBalances.Where(b => b.ItemId == i.Id).Sum(b => (decimal?)b.Quantity) ?? 0m })
            .Where(x => x.Qty <= x.i.ReorderLevel)
            .OrderBy(x => x.i.Code)
            .Select(x => new LowStockRow(x.i.Id, x.i.Code, x.i.NameAr, x.i.NameEn, x.i.ReorderLevel, x.Qty))
            .ToListAsync(ct);

    // ---------- helpers ----------
    private static (int Page, int Size) Paging(int page, int size, int max = 200) => (Math.Max(1, page), Math.Clamp(size, 1, max));

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static void EnsureDraft(DocumentStatus s)
    {
        if (s != DocumentStatus.Draft) throw new BusinessRuleException("Only draft documents can be edited or confirmed.");
    }
}
