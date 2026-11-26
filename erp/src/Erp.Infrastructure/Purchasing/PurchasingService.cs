using Erp.Application.Common;
using Erp.Application.Purchasing;
using Erp.Domain.Entities;
using Erp.Infrastructure.Accounting;
using Erp.Infrastructure.Common;
using Erp.Infrastructure.Inventory;
using Erp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

using static Erp.Infrastructure.Common.DocumentRules;

namespace Erp.Infrastructure.Purchasing;

public class PurchasingService(AppDbContext db, ICurrentUser user, StockPoster poster, JournalPoster gl) : IPurchasingService
{
    // =====================================================================
    //  Shared helpers
    // =====================================================================

    private static string Like(string? q) => q!.Trim();

    private async Task SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new ConcurrencyConflictException(); }
    }

    private async Task EnsureSupplierAsync(int supplierId, CancellationToken ct)
    {
        if (!await db.Suppliers.AnyAsync(s => s.Id == supplierId && s.IsActive, ct))
            throw new BusinessRuleException("The supplier does not exist or is inactive.");
    }

    /// <summary>
    /// Checks the order link of a receipt/invoice draft: the order must be confirmed, same supplier and currency, and every
    /// line must point at a distinct line of that order for the same item.
    /// </summary>
    private async Task<PurchaseOrder?> ValidateOrderLinkAsync(int? orderId, int supplierId, int currencyId,
        IReadOnlyCollection<(int? OrderLineId, int ItemId)> lines, CancellationToken ct)
    {
        if (orderId is null)
        {
            if (lines.Any(l => l.OrderLineId is not null))
                throw new BusinessRuleException("A line can reference an order line only when the document is linked to a purchase order.");
            return null;
        }

        var po = await db.PurchaseOrders.AsNoTracking().Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == orderId, ct)
                 ?? throw new BusinessRuleException("The purchase order does not exist.");
        if (po.Status != DocumentStatus.Confirmed) throw new BusinessRuleException("Only a confirmed purchase order can be received or invoiced.");
        if (po.SupplierId != supplierId) throw new BusinessRuleException("The supplier differs from the purchase order's supplier.");
        if (po.CurrencyId != currencyId) throw new BusinessRuleException("The currency differs from the purchase order's currency.");

        var used = new HashSet<int>();
        foreach (var (lineId, itemId) in lines)
        {
            if (lineId is null) throw new BusinessRuleException("Every line must reference a purchase order line when the document is linked to an order.");
            var pl = po.Lines.FirstOrDefault(x => x.Id == lineId) ?? throw new BusinessRuleException("An order line does not belong to the purchase order.");
            if (pl.ItemId != itemId) throw new BusinessRuleException("A line's item does not match its purchase order line.");
            if (!used.Add(pl.Id)) throw new BusinessRuleException("A purchase order line may appear only once per document.");
        }
        return po;
    }

    private static ProgressState State(bool any, bool all) => !any ? ProgressState.None : all ? ProgressState.Full : ProgressState.Partial;

    // =====================================================================
    //  Purchase orders
    // =====================================================================

    public async Task<PagedResult<PurchaseOrderRow>> ListOrdersAsync(PurchasingFilter f, CancellationToken ct)
    {
        var q = db.PurchaseOrders.AsNoTracking().AsQueryable();
        if (f.Status is { } st) q = q.Where(p => p.Status == st);
        if (f.SupplierId is { } sid) q = q.Where(p => p.SupplierId == sid);
        if (f.PendingOnly) q = q.Where(p => p.Status == DocumentStatus.Confirmed && p.Lines.Any(l => l.ReceivedQuantity < l.Quantity));
        if (!string.IsNullOrWhiteSpace(f.Q)) { var s = Like(f.Q); q = q.Where(p => (p.Number != null && p.Number.Contains(s)) || (p.Notes != null && p.Notes.Contains(s))); }

        var (page, size) = Paging(f.Page, f.PageSize);
        var total = await q.CountAsync(ct);
        var raw = await q.OrderByDescending(p => p.Date).ThenByDescending(p => p.Id).Skip((page - 1) * size).Take(size)
            .Select(p => new
            {
                p.Id, p.Number, p.Date, p.ExpectedDate, p.SupplierId, p.CurrencyId, p.Status, p.TotalAmount,
                AnyReceived = p.Lines.Any(l => l.ReceivedQuantity > 0), AllReceived = p.Lines.All(l => l.ReceivedQuantity >= l.Quantity),
                AnyInvoiced = p.Lines.Any(l => l.InvoicedQuantity > 0), AllInvoiced = p.Lines.All(l => l.InvoicedQuantity >= l.Quantity),
                Count = p.Lines.Count,
            }).ToListAsync(ct);

        var rows = raw.Select(p => new PurchaseOrderRow(p.Id, p.Number, p.Date, p.ExpectedDate, p.SupplierId, p.CurrencyId, p.Status, p.TotalAmount,
            State(p.AnyReceived, p.AllReceived), State(p.AnyInvoiced, p.AllInvoiced), p.Count)).ToList();
        return new PagedResult<PurchaseOrderRow>(rows, total, page, size);
    }

    public async Task<PurchaseOrder> GetOrderAsync(int id, CancellationToken ct) =>
        await db.PurchaseOrders.AsNoTracking().Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == id, ct)
        ?? throw new NotFoundException("Purchase order not found.");

    private async Task<PurchaseOrder> LoadOrderAsync(int id, CancellationToken ct) =>
        await db.PurchaseOrders.Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == id, ct)
        ?? throw new NotFoundException("Purchase order not found.");

    private static List<PurchaseOrderLine> OrderLines(IEnumerable<PoLineInput> lines) =>
        lines.Select(l => new PurchaseOrderLine { ItemId = l.ItemId, Quantity = l.Quantity, UnitPrice = l.UnitPrice }).ToList();

    public async Task<PurchaseOrder> CreateOrderAsync(PurchaseOrderInput i, CancellationToken ct)
    {
        await EnsureSupplierAsync(i.SupplierId, ct);
        await db.EnsureItemsAsync(i.Lines.Select(l => l.ItemId), ct);
        var rate = await db.ResolveRateAsync(i.CurrencyId, i.Date, i.ExchangeRate, null, ct);

        var po = new PurchaseOrder
        {
            Date = i.Date, ExpectedDate = i.ExpectedDate, SupplierId = i.SupplierId, CurrencyId = i.CurrencyId, ExchangeRate = rate,
            Notes = Clean(i.Notes), Status = DocumentStatus.Draft, Lines = OrderLines(i.Lines),
            TotalAmount = i.Lines.Sum(l => R4(l.Quantity * l.UnitPrice)),
        };
        db.PurchaseOrders.Add(po);
        await SaveAsync(ct);
        po.Number = $"PO-{po.Id:D6}";
        await SaveAsync(ct);
        return po;
    }

    public async Task<PurchaseOrder> UpdateOrderAsync(int id, PurchaseOrderInput i, CancellationToken ct)
    {
        var po = await LoadOrderAsync(id, ct);
        EnsureDraft(po.Status);
        await EnsureSupplierAsync(i.SupplierId, ct);
        await db.EnsureItemsAsync(i.Lines.Select(l => l.ItemId), ct);
        var rate = await db.ResolveRateAsync(i.CurrencyId, i.Date, i.ExchangeRate, po.CurrencyId == i.CurrencyId ? po.ExchangeRate : null, ct);

        po.Date = i.Date; po.ExpectedDate = i.ExpectedDate; po.SupplierId = i.SupplierId; po.CurrencyId = i.CurrencyId;
        po.ExchangeRate = rate; po.Notes = Clean(i.Notes);
        db.PurchaseOrderLines.RemoveRange(po.Lines);
        po.Lines = OrderLines(i.Lines);
        po.TotalAmount = i.Lines.Sum(l => R4(l.Quantity * l.UnitPrice));
        await SaveAsync(ct);
        return po;
    }

    public async Task<PurchaseOrder> ConfirmOrderAsync(int id, CancellationToken ct)
    {
        var po = await LoadOrderAsync(id, ct);
        EnsureDraft(po.Status);
        await EnsureSupplierAsync(po.SupplierId, ct);
        await db.EnsureItemsAsync(po.Lines.Select(l => l.ItemId), ct);
        po.Status = DocumentStatus.Confirmed;
        po.ConfirmedAt = DateTime.UtcNow;
        po.ConfirmedBy = user.UserName;
        await SaveAsync(ct);
        return po;
    }

    public async Task<PurchaseOrder> CancelOrderAsync(int id, CancellationToken ct)
    {
        var po = await LoadOrderAsync(id, ct);
        if (po.Status == DocumentStatus.Cancelled) throw new BusinessRuleException("The document is already cancelled.");
        if (po.Lines.Any(l => l.ReceivedQuantity > 0 || l.InvoicedQuantity > 0))
            throw new BusinessRuleException("This order already has confirmed receipts or invoices. Cancel those first.");
        po.Status = DocumentStatus.Cancelled;
        await SaveAsync(ct);
        return po;
    }

    // =====================================================================
    //  Goods receipts
    // =====================================================================

    public async Task<PagedResult<GoodsReceiptRow>> ListReceiptsAsync(PurchasingFilter f, CancellationToken ct)
    {
        var q = db.GoodsReceipts.AsNoTracking().AsQueryable();
        if (f.Status is { } st) q = q.Where(r => r.Status == st);
        if (f.SupplierId is { } sid) q = q.Where(r => r.SupplierId == sid);
        if (!string.IsNullOrWhiteSpace(f.Q)) { var s = Like(f.Q); q = q.Where(r => (r.Number != null && r.Number.Contains(s)) || (r.Notes != null && r.Notes.Contains(s))); }

        var (page, size) = Paging(f.Page, f.PageSize);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(r => r.Date).ThenByDescending(r => r.Id).Skip((page - 1) * size).Take(size)
            .Select(r => new GoodsReceiptRow(r.Id, r.Number, r.Date, r.SupplierId, r.WarehouseId, r.PurchaseOrderId, r.CurrencyId, r.Status, r.TotalAmount, r.Lines.Count))
            .ToListAsync(ct);
        return new PagedResult<GoodsReceiptRow>(rows, total, page, size);
    }

    public async Task<GoodsReceipt> GetReceiptAsync(int id, CancellationToken ct) =>
        await db.GoodsReceipts.AsNoTracking().Include(r => r.Lines).FirstOrDefaultAsync(r => r.Id == id, ct)
        ?? throw new NotFoundException("Goods receipt not found.");

    private async Task<GoodsReceipt> LoadReceiptAsync(int id, CancellationToken ct) =>
        await db.GoodsReceipts.Include(r => r.Lines).FirstOrDefaultAsync(r => r.Id == id, ct)
        ?? throw new NotFoundException("Goods receipt not found.");

    private static List<GoodsReceiptLine> ReceiptLines(IEnumerable<ReceiptLineInput> lines) =>
        lines.Select(l => new GoodsReceiptLine { PurchaseOrderLineId = l.PurchaseOrderLineId, ItemId = l.ItemId, Quantity = l.Quantity, UnitPrice = l.UnitPrice }).ToList();

    private async Task ValidateReceiptAsync(GoodsReceiptInput i, CancellationToken ct)
    {
        await EnsureSupplierAsync(i.SupplierId, ct);
        await db.EnsureWarehouseAsync(i.WarehouseId, ct);
        await db.EnsureItemsAsync(i.Lines.Select(l => l.ItemId), ct);
        await ValidateOrderLinkAsync(i.PurchaseOrderId, i.SupplierId, i.CurrencyId, i.Lines.Select(l => (l.PurchaseOrderLineId, l.ItemId)).ToList(), ct);
    }

    public async Task<GoodsReceipt> CreateReceiptAsync(GoodsReceiptInput i, CancellationToken ct)
    {
        await ValidateReceiptAsync(i, ct);
        var order = i.PurchaseOrderId is { } oid ? await db.PurchaseOrders.AsNoTracking().FirstAsync(p => p.Id == oid, ct) : null;
        var rate = await db.ResolveRateAsync(i.CurrencyId, i.Date, i.ExchangeRate, order?.ExchangeRate, ct);

        var r = new GoodsReceipt
        {
            Date = i.Date, SupplierId = i.SupplierId, WarehouseId = i.WarehouseId, PurchaseOrderId = i.PurchaseOrderId,
            CurrencyId = i.CurrencyId, ExchangeRate = rate, Notes = Clean(i.Notes), Status = DocumentStatus.Draft,
            Lines = ReceiptLines(i.Lines), TotalAmount = i.Lines.Sum(l => R4(l.Quantity * l.UnitPrice)),
        };
        db.GoodsReceipts.Add(r);
        await SaveAsync(ct);
        r.Number = $"GRN-{r.Id:D6}";
        await SaveAsync(ct);
        return r;
    }

    public async Task<GoodsReceipt> UpdateReceiptAsync(int id, GoodsReceiptInput i, CancellationToken ct)
    {
        var r = await LoadReceiptAsync(id, ct);
        EnsureDraft(r.Status);
        await ValidateReceiptAsync(i, ct);
        var order = i.PurchaseOrderId is { } oid ? await db.PurchaseOrders.AsNoTracking().FirstAsync(p => p.Id == oid, ct) : null;
        var rate = await db.ResolveRateAsync(i.CurrencyId, i.Date, i.ExchangeRate,
            order?.ExchangeRate ?? (r.CurrencyId == i.CurrencyId ? r.ExchangeRate : null), ct);

        r.Date = i.Date; r.SupplierId = i.SupplierId; r.WarehouseId = i.WarehouseId; r.PurchaseOrderId = i.PurchaseOrderId;
        r.CurrencyId = i.CurrencyId; r.ExchangeRate = rate; r.Notes = Clean(i.Notes);
        db.GoodsReceiptLines.RemoveRange(r.Lines);
        r.Lines = ReceiptLines(i.Lines);
        r.TotalAmount = i.Lines.Sum(l => R4(l.Quantity * l.UnitPrice));
        await SaveAsync(ct);
        return r;
    }

    public async Task<GoodsReceipt> ConfirmReceiptAsync(int id, CancellationToken ct)
    {
        var r = await LoadReceiptAsync(id, ct);
        EnsureDraft(r.Status);
        await EnsureSupplierAsync(r.SupplierId, ct);
        await db.EnsureWarehouseAsync(r.WarehouseId, ct);
        await db.EnsureItemsAsync(r.Lines.Select(l => l.ItemId), ct);

        PurchaseOrder? po = null;
        if (r.PurchaseOrderId is { } oid)
        {
            po = await LoadOrderAsync(oid, ct); // tracked: its lines' received quantities change with this receipt
            if (po.Status != DocumentStatus.Confirmed) throw new BusinessRuleException("The linked purchase order is no longer confirmed.");
        }

        var src = new PostingSource(r.Date, DocumentTypes.GoodsReceipt, r.Id, r.Number);
        var stockValue = 0m;
        foreach (var line in r.Lines)
        {
            stockValue += R4(line.Quantity * R4(line.UnitPrice * r.ExchangeRate));
            if (line.PurchaseOrderLineId is { } plid)
            {
                var pl = po?.Lines.FirstOrDefault(x => x.Id == plid) ?? throw new BusinessRuleException("An order line does not belong to the purchase order.");
                var remaining = pl.Quantity - pl.ReceivedQuantity;
                if (line.Quantity > remaining)
                {
                    var code = await db.Items.Where(x => x.Id == line.ItemId).Select(x => x.Code).FirstAsync(ct);
                    throw new BusinessRuleException($"Receipt quantity for item {code} exceeds the remaining ordered quantity ({remaining:0.####}).");
                }
                pl.ReceivedQuantity += line.Quantity;
            }
            // Stock is valued in local currency: unit price x the receipt's exchange rate.
            await poster.ReceiveAsync(src, line.ItemId, r.WarehouseId, line.Quantity, R4(line.UnitPrice * r.ExchangeRate),
                MovementType.PurchaseReceipt, null, ct);
        }

        await gl.PostAsync(SourceTypes.GoodsReceipt, r.Id, r.Date, $"Goods receipt {r.Number}", ct,
            new PostLine(AccountRoles.Inventory, stockValue, 0),
            new PostLine(AccountRoles.GoodsReceivedNotInvoiced, 0, stockValue));

        r.Status = DocumentStatus.Confirmed;
        r.ConfirmedAt = DateTime.UtcNow;
        r.ConfirmedBy = user.UserName;
        await SaveAsync(ct);
        return r;
    }

    public async Task<GoodsReceipt> CancelReceiptAsync(int id, CancellationToken ct)
    {
        var r = await LoadReceiptAsync(id, ct);
        if (r.Status == DocumentStatus.Cancelled) throw new BusinessRuleException("The document is already cancelled.");

        if (r.Status == DocumentStatus.Confirmed)
        {
            if (r.PurchaseOrderId is { } oid)
            {
                var po = await LoadOrderAsync(oid, ct);
                foreach (var line in r.Lines.Where(l => l.PurchaseOrderLineId is not null))
                {
                    var pl = po.Lines.First(x => x.Id == line.PurchaseOrderLineId);
                    if (pl.ReceivedQuantity - line.Quantity < pl.InvoicedQuantity)
                        throw new BusinessRuleException("Invoices have already been confirmed against this receipt's quantities. Cancel them first.");
                    pl.ReceivedQuantity -= line.Quantity;
                }
            }
            await poster.ReverseDocumentAsync(DocumentTypes.GoodsReceipt, r.Id, r.Number, ct);
            await gl.ReverseAsync(SourceTypes.GoodsReceipt, r.Id, $"Cancelled goods receipt {r.Number}", ct);
        }
        r.Status = DocumentStatus.Cancelled;
        await SaveAsync(ct);
        return r;
    }

    // =====================================================================
    //  Purchase invoices
    // =====================================================================

    public async Task<PagedResult<PurchaseInvoiceRow>> ListInvoicesAsync(PurchasingFilter f, CancellationToken ct)
    {
        var q = db.PurchaseInvoices.AsNoTracking().AsQueryable();
        if (f.Status is { } st) q = q.Where(i => i.Status == st);
        if (f.SupplierId is { } sid) q = q.Where(i => i.SupplierId == sid);
        if (!string.IsNullOrWhiteSpace(f.Q))
        {
            var s = Like(f.Q);
            q = q.Where(i => (i.Number != null && i.Number.Contains(s)) || (i.SupplierInvoiceNumber != null && i.SupplierInvoiceNumber.Contains(s)));
        }

        var (page, size) = Paging(f.Page, f.PageSize);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(i => i.Date).ThenByDescending(i => i.Id).Skip((page - 1) * size).Take(size)
            .Select(i => new PurchaseInvoiceRow(i.Id, i.Number, i.Date, i.DueDate, i.SupplierId, i.SupplierInvoiceNumber, i.PurchaseOrderId, i.CurrencyId, i.Status, i.TotalAmount))
            .ToListAsync(ct);
        return new PagedResult<PurchaseInvoiceRow>(rows, total, page, size);
    }

    public async Task<PurchaseInvoice> GetInvoiceAsync(int id, CancellationToken ct) =>
        await db.PurchaseInvoices.AsNoTracking().Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id, ct)
        ?? throw new NotFoundException("Purchase invoice not found.");

    private async Task<PurchaseInvoice> LoadInvoiceAsync(int id, CancellationToken ct) =>
        await db.PurchaseInvoices.Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id, ct)
        ?? throw new NotFoundException("Purchase invoice not found.");

    private static List<PurchaseInvoiceLine> InvoiceLines(IEnumerable<InvoiceLineInput> lines) =>
        lines.Select(l => new PurchaseInvoiceLine { PurchaseOrderLineId = l.PurchaseOrderLineId, ItemId = l.ItemId, Quantity = l.Quantity, UnitPrice = l.UnitPrice }).ToList();

    private async Task ValidateInvoiceAsync(PurchaseInvoiceInput i, CancellationToken ct)
    {
        await EnsureSupplierAsync(i.SupplierId, ct);
        await db.EnsureItemsAsync(i.Lines.Select(l => l.ItemId), ct);
        await ValidateOrderLinkAsync(i.PurchaseOrderId, i.SupplierId, i.CurrencyId, i.Lines.Select(l => (l.PurchaseOrderLineId, l.ItemId)).ToList(), ct);
    }

    public async Task<PurchaseInvoice> CreateInvoiceAsync(PurchaseInvoiceInput i, CancellationToken ct)
    {
        await ValidateInvoiceAsync(i, ct);
        var order = i.PurchaseOrderId is { } oid ? await db.PurchaseOrders.AsNoTracking().FirstAsync(p => p.Id == oid, ct) : null;
        var rate = await db.ResolveRateAsync(i.CurrencyId, i.Date, i.ExchangeRate, order?.ExchangeRate, ct);

        var inv = new PurchaseInvoice
        {
            Date = i.Date, DueDate = i.DueDate, SupplierId = i.SupplierId, SupplierInvoiceNumber = Clean(i.SupplierInvoiceNumber),
            PurchaseOrderId = i.PurchaseOrderId, CurrencyId = i.CurrencyId, ExchangeRate = rate, Notes = Clean(i.Notes),
            Status = DocumentStatus.Draft, Lines = InvoiceLines(i.Lines), TotalAmount = i.Lines.Sum(l => R4(l.Quantity * l.UnitPrice)),
        };
        db.PurchaseInvoices.Add(inv);
        await SaveAsync(ct);
        inv.Number = $"PINV-{inv.Id:D6}";
        await SaveAsync(ct);
        return inv;
    }

    public async Task<PurchaseInvoice> UpdateInvoiceAsync(int id, PurchaseInvoiceInput i, CancellationToken ct)
    {
        var inv = await LoadInvoiceAsync(id, ct);
        EnsureDraft(inv.Status);
        await ValidateInvoiceAsync(i, ct);
        var order = i.PurchaseOrderId is { } oid ? await db.PurchaseOrders.AsNoTracking().FirstAsync(p => p.Id == oid, ct) : null;
        var rate = await db.ResolveRateAsync(i.CurrencyId, i.Date, i.ExchangeRate,
            order?.ExchangeRate ?? (inv.CurrencyId == i.CurrencyId ? inv.ExchangeRate : null), ct);

        inv.Date = i.Date; inv.DueDate = i.DueDate; inv.SupplierId = i.SupplierId; inv.SupplierInvoiceNumber = Clean(i.SupplierInvoiceNumber);
        inv.PurchaseOrderId = i.PurchaseOrderId; inv.CurrencyId = i.CurrencyId; inv.ExchangeRate = rate; inv.Notes = Clean(i.Notes);
        db.PurchaseInvoiceLines.RemoveRange(inv.Lines);
        inv.Lines = InvoiceLines(i.Lines);
        inv.TotalAmount = i.Lines.Sum(l => R4(l.Quantity * l.UnitPrice));
        await SaveAsync(ct);
        return inv;
    }

    public async Task<PurchaseInvoice> ConfirmInvoiceAsync(int id, CancellationToken ct)
    {
        var inv = await LoadInvoiceAsync(id, ct);
        EnsureDraft(inv.Status);
        await EnsureSupplierAsync(inv.SupplierId, ct);
        await db.EnsureItemsAsync(inv.Lines.Select(l => l.ItemId), ct);

        if (inv.PurchaseOrderId is { } oid)
        {
            var po = await LoadOrderAsync(oid, ct);
            if (po.Status != DocumentStatus.Confirmed) throw new BusinessRuleException("The linked purchase order is no longer confirmed.");
            foreach (var line in inv.Lines)
            {
                var pl = po.Lines.FirstOrDefault(x => x.Id == line.PurchaseOrderLineId) ?? throw new BusinessRuleException("An order line does not belong to the purchase order.");
                // You are billed for goods that have arrived: invoiced quantity may not run ahead of received quantity.
                var billable = pl.ReceivedQuantity - pl.InvoicedQuantity;
                if (line.Quantity > billable)
                {
                    var code = await db.Items.Where(x => x.Id == line.ItemId).Select(x => x.Code).FirstAsync(ct);
                    throw new BusinessRuleException($"Invoice quantity for item {code} exceeds the received, not-yet-invoiced quantity ({billable:0.####}).");
                }
                pl.InvoicedQuantity += line.Quantity;
            }
        }

        var payable = R4(inv.TotalAmount * inv.ExchangeRate);
        await gl.PostAsync(SourceTypes.PurchaseInvoice, inv.Id, inv.Date, $"Purchase invoice {inv.Number}", ct,
            new PostLine(AccountRoles.GoodsReceivedNotInvoiced, payable, 0),
            new PostLine(AccountRoles.Payables, 0, payable, PartyType.Supplier, inv.SupplierId));

        inv.Status = DocumentStatus.Confirmed;
        inv.ConfirmedAt = DateTime.UtcNow;
        inv.ConfirmedBy = user.UserName;
        await SaveAsync(ct);
        return inv;
    }

    public async Task<PurchaseInvoice> CancelInvoiceAsync(int id, CancellationToken ct)
    {
        var inv = await LoadInvoiceAsync(id, ct);
        if (inv.Status == DocumentStatus.Cancelled) throw new BusinessRuleException("The document is already cancelled.");

        if (inv.Status == DocumentStatus.Confirmed && inv.PurchaseOrderId is { } oid)
        {
            var po = await LoadOrderAsync(oid, ct);
            foreach (var line in inv.Lines)
                po.Lines.First(x => x.Id == line.PurchaseOrderLineId).InvoicedQuantity -= line.Quantity;
        }
        if (inv.Status == DocumentStatus.Confirmed) await gl.ReverseAsync(SourceTypes.PurchaseInvoice, inv.Id, $"Cancelled purchase invoice {inv.Number}", ct);
        inv.Status = DocumentStatus.Cancelled;
        await SaveAsync(ct);
        return inv;
    }

    // =====================================================================
    //  Supplier payments
    // =====================================================================

    public async Task<PagedResult<SupplierPayment>> ListPaymentsAsync(PurchasingFilter f, CancellationToken ct)
    {
        var q = db.SupplierPayments.AsNoTracking().AsQueryable();
        if (f.Status is { } st) q = q.Where(p => p.Status == st);
        if (f.SupplierId is { } sid) q = q.Where(p => p.SupplierId == sid);
        if (!string.IsNullOrWhiteSpace(f.Q))
        {
            var s = Like(f.Q);
            q = q.Where(p => (p.Number != null && p.Number.Contains(s)) || (p.Reference != null && p.Reference.Contains(s)));
        }

        var (page, size) = Paging(f.Page, f.PageSize);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(p => p.Date).ThenByDescending(p => p.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new PagedResult<SupplierPayment>(rows, total, page, size);
    }

    public async Task<SupplierPayment> GetPaymentAsync(int id, CancellationToken ct) =>
        await db.SupplierPayments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("Payment not found.");

    private async Task<SupplierPayment> LoadPaymentAsync(int id, CancellationToken ct) =>
        await db.SupplierPayments.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("Payment not found.");

    public async Task<SupplierPayment> CreatePaymentAsync(SupplierPaymentInput i, CancellationToken ct)
    {
        await EnsureSupplierAsync(i.SupplierId, ct);
        var rate = await db.ResolveRateAsync(i.CurrencyId, i.Date, i.ExchangeRate, null, ct);
        var p = new SupplierPayment
        {
            Date = i.Date, SupplierId = i.SupplierId, CurrencyId = i.CurrencyId, ExchangeRate = rate, Amount = R4(i.Amount),
            Method = i.Method, Reference = Clean(i.Reference), Notes = Clean(i.Notes), Status = DocumentStatus.Draft,
        };
        db.SupplierPayments.Add(p);
        await SaveAsync(ct);
        p.Number = $"PAY-{p.Id:D6}";
        await SaveAsync(ct);
        return p;
    }

    public async Task<SupplierPayment> UpdatePaymentAsync(int id, SupplierPaymentInput i, CancellationToken ct)
    {
        var p = await LoadPaymentAsync(id, ct);
        EnsureDraft(p.Status);
        await EnsureSupplierAsync(i.SupplierId, ct);
        var rate = await db.ResolveRateAsync(i.CurrencyId, i.Date, i.ExchangeRate, p.CurrencyId == i.CurrencyId ? p.ExchangeRate : null, ct);
        p.Date = i.Date; p.SupplierId = i.SupplierId; p.CurrencyId = i.CurrencyId; p.ExchangeRate = rate; p.Amount = R4(i.Amount);
        p.Method = i.Method; p.Reference = Clean(i.Reference); p.Notes = Clean(i.Notes);
        await SaveAsync(ct);
        return p;
    }

    public async Task<SupplierPayment> ConfirmPaymentAsync(int id, CancellationToken ct)
    {
        var p = await LoadPaymentAsync(id, ct);
        EnsureDraft(p.Status);
        await EnsureSupplierAsync(p.SupplierId, ct);
        var paid = R4(p.Amount * p.ExchangeRate);
        await gl.PostAsync(SourceTypes.SupplierPayment, p.Id, p.Date, $"Supplier payment {p.Number}", ct,
            new PostLine(AccountRoles.Payables, paid, 0, PartyType.Supplier, p.SupplierId),
            new PostLine(p.Method == PaymentMethod.Cash ? AccountRoles.Cash : AccountRoles.Bank, 0, paid));

        p.Status = DocumentStatus.Confirmed;
        p.ConfirmedAt = DateTime.UtcNow;
        p.ConfirmedBy = user.UserName;
        await SaveAsync(ct);
        return p;
    }

    public async Task<SupplierPayment> CancelPaymentAsync(int id, CancellationToken ct)
    {
        var p = await LoadPaymentAsync(id, ct);
        if (p.Status == DocumentStatus.Cancelled) throw new BusinessRuleException("The document is already cancelled.");
        if (p.Status == DocumentStatus.Confirmed) await gl.ReverseAsync(SourceTypes.SupplierPayment, p.Id, $"Cancelled supplier payment {p.Number}", ct);
        p.Status = DocumentStatus.Cancelled;
        await SaveAsync(ct);
        return p;
    }

    // =====================================================================
    //  Reports
    // =====================================================================

    public async Task<SupplierStatement> GetStatementAsync(int supplierId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        if (!await db.Suppliers.AnyAsync(s => s.Id == supplierId, ct)) throw new NotFoundException("Supplier not found.");

        var invoices = await db.PurchaseInvoices.AsNoTracking()
            .Where(i => i.SupplierId == supplierId && i.Status == DocumentStatus.Confirmed)
            .Select(i => new { i.Id, i.Date, i.Number, Ref = i.SupplierInvoiceNumber, i.CurrencyId, i.TotalAmount, i.ExchangeRate }).ToListAsync(ct);
        var payments = await db.SupplierPayments.AsNoTracking()
            .Where(p => p.SupplierId == supplierId && p.Status == DocumentStatus.Confirmed)
            .Select(p => new { p.Id, p.Date, p.Number, Ref = p.Reference, p.CurrencyId, p.Amount, p.ExchangeRate }).ToListAsync(ct);

        // (date, kind order, id) keeps same-day entries stable: invoices before payments.
        var entries = invoices.Select(i => (i.Date, Order: 0, i.Id, Type: "Invoice", i.Number, i.Ref, i.CurrencyId, Amount: i.TotalAmount, i.ExchangeRate, Debit: 0m, Credit: R4(i.TotalAmount * i.ExchangeRate)))
            .Concat(payments.Select(p => (p.Date, Order: 1, p.Id, Type: "Payment", p.Number, p.Ref, p.CurrencyId, Amount: p.Amount, p.ExchangeRate, Debit: R4(p.Amount * p.ExchangeRate), Credit: 0m)))
            .OrderBy(e => e.Date).ThenBy(e => e.Order).ThenBy(e => e.Id).ToList();

        var opening = from is { } f ? entries.Where(e => e.Date < f).Sum(e => e.Credit - e.Debit) : 0m;
        var running = opening;
        var rows = new List<StatementRow>();
        foreach (var e in entries.Where(e => (from is null || e.Date >= from) && (to is null || e.Date <= to)))
        {
            running += e.Credit - e.Debit;
            rows.Add(new StatementRow(e.Date, e.Type, e.Number, e.Ref, e.CurrencyId, e.Amount, e.ExchangeRate, e.Debit, e.Credit, running));
        }
        return new SupplierStatement(supplierId, from, to, opening, rows, rows.Sum(r => r.Credit), rows.Sum(r => r.Debit), running);
    }

    public async Task<IReadOnlyList<SupplierBalanceRow>> GetSupplierBalancesAsync(CancellationToken ct)
    {
        var invoiced = await db.PurchaseInvoices.AsNoTracking().Where(i => i.Status == DocumentStatus.Confirmed)
            .GroupBy(i => i.SupplierId).Select(g => new { Id = g.Key, Sum = g.Sum(i => Math.Round(i.TotalAmount * i.ExchangeRate, 4)) }).ToListAsync(ct);
        var paid = await db.SupplierPayments.AsNoTracking().Where(p => p.Status == DocumentStatus.Confirmed)
            .GroupBy(p => p.SupplierId).Select(g => new { Id = g.Key, Sum = g.Sum(p => Math.Round(p.Amount * p.ExchangeRate, 4)) }).ToListAsync(ct);

        var ids = invoiced.Select(x => x.Id).Union(paid.Select(x => x.Id)).ToList();
        var suppliers = await db.Suppliers.AsNoTracking().Where(s => ids.Contains(s.Id)).ToListAsync(ct);

        return suppliers.Select(s =>
        {
            var inv = invoiced.FirstOrDefault(x => x.Id == s.Id)?.Sum ?? 0m;
            var pay = paid.FirstOrDefault(x => x.Id == s.Id)?.Sum ?? 0m;
            return new SupplierBalanceRow(s.Id, s.Code, s.NameAr, s.NameEn, inv, pay, inv - pay);
        }).OrderByDescending(r => r.Balance).ThenBy(r => r.Code).ToList();
    }
}
