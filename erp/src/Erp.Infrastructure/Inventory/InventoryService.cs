using Erp.Application.Common;
using Erp.Application.Inventory;
using Erp.Domain.Entities;
using Erp.Domain.Inventory;
using Erp.Infrastructure.Accounting;
using Erp.Infrastructure.Common;
using Erp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Erp.Infrastructure.Inventory;

public class InventoryService(AppDbContext db, ICurrentUser user, StockPoster poster, JournalPoster gl, DocumentNumbering numbers) : IInventoryService
{
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
        t.Number = await numbers.NextAsync(SequenceKeys.Transfer, ct);
        await EnsureActiveAsync([t.FromWarehouseId, t.ToWarehouseId], t.Lines.Select(l => l.ItemId), ct);

        var src = new PostingSource(t.Date, DocumentTypes.Transfer, t.Id, t.Number);
        foreach (var line in t.Lines)
        {
            var cost = await poster.IssueAsync(src, line.ItemId, t.FromWarehouseId, line.Quantity, MovementType.TransferOut, null, ct);
            await poster.ReceiveAsync(src, line.ItemId, t.ToWarehouseId, line.Quantity, cost, MovementType.TransferIn, null, ct);
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
        if (t.Status == DocumentStatus.Confirmed) await poster.ReverseDocumentAsync(DocumentTypes.Transfer, t.Id, t.Number, ct);
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
        a.Number = await numbers.NextAsync(SequenceKeys.Adjustment, ct);
        await EnsureActiveAsync([a.WarehouseId], a.Lines.Select(l => l.ItemId), ct);

        var src = new PostingSource(a.Date, DocumentTypes.Adjustment, a.Id, a.Number);
        var gain = 0m;
        var loss = 0m;
        foreach (var line in a.Lines)
        {
            var balance = await poster.BalanceAsync(line.ItemId, a.WarehouseId, ct);
            var diff = line.CountedQuantity - balance.Quantity;
            line.SystemQuantity = balance.Quantity;
            line.Difference = diff;

            if (diff > 0)
            {
                // surplus enters at the stated cost, else the current average, else the item's standard cost
                var cost = line.UnitCost
                           ?? (balance.AverageCost > 0 ? balance.AverageCost
                               : await db.Items.Where(i => i.Id == line.ItemId).Select(i => i.CostPrice).FirstAsync(ct));
                await poster.ReceiveAsync(src, line.ItemId, a.WarehouseId, diff, cost, MovementType.AdjustmentIn, null, ct);
                gain += DocumentRules.R4(diff * cost);
            }
            else if (diff < 0)
            {
                var lostCost = await poster.IssueAsync(src, line.ItemId, a.WarehouseId, -diff, MovementType.AdjustmentOut, null, ct);
                loss += DocumentRules.R4(-diff * lostCost);
            }
        }
        // Surplus: Dr Inventory / Cr (opening equity for an opening balance, else adjustments). Shortage: the other way round.
        var counterpart = a.Reason == AdjustmentReason.Opening ? AccountRoles.OpeningEquity : AccountRoles.InventoryAdjustments;
        await gl.PostAsync(SourceTypes.StockAdjustment, a.Id, a.Date, $"Stock adjustment {a.Number}", ct,
            new PostLine(AccountRoles.Inventory, gain, 0),
            new PostLine(counterpart, 0, gain),
            new PostLine(AccountRoles.InventoryAdjustments, loss, 0),
            new PostLine(AccountRoles.Inventory, 0, loss));

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
        if (a.Status == DocumentStatus.Confirmed)
        {
            await poster.ReverseDocumentAsync(DocumentTypes.Adjustment, a.Id, a.Number, ct);
            await gl.ReverseAsync(SourceTypes.StockAdjustment, a.Id, $"Cancelled stock adjustment {a.Number}", ct);
        }
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
        if (!string.IsNullOrWhiteSpace(f.Q))
        {
            var s = f.Q.Trim();
            q = q.Where(x => x.i.Code.Contains(s) || x.i.NameAr.Contains(s) || x.i.NameEn.Contains(s) || (x.m.DocumentNumber != null && x.m.DocumentNumber.Contains(s))
                             || x.w.Code.Contains(s) || x.w.NameAr.Contains(s) || x.w.NameEn.Contains(s));
        }

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
