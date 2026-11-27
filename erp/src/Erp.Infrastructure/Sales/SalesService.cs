using Erp.Application.Common;
using Erp.Application.Sales;
using Erp.Application.Settlement;
using Erp.Domain.Entities;
using Erp.Infrastructure.Accounting;
using Erp.Infrastructure.Common;
using Erp.Infrastructure.Inventory;
using Erp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

using static Erp.Infrastructure.Common.DocumentRules;

namespace Erp.Infrastructure.Sales;

public class SalesService(AppDbContext db, ICurrentUser user, StockPoster poster, JournalPoster gl, DocumentNumbering numbers) : ISalesService
{
    // =====================================================================
    //  Shared helpers
    // =====================================================================

    private static string Like(string? q) => q!.Trim();
    private static ProgressState State(bool any, bool all) => !any ? ProgressState.None : all ? ProgressState.Full : ProgressState.Partial;

    private async Task SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new ConcurrencyConflictException(); }
    }

    private async Task EnsureCustomerAsync(int customerId, CancellationToken ct)
    {
        if (!await db.Customers.AnyAsync(c => c.Id == customerId && c.IsActive, ct))
            throw new BusinessRuleException("The customer does not exist or is inactive.");
    }

    /// <summary>Order link of a delivery/invoice draft: confirmed order, same customer and currency, each line a distinct order line for the same item.</summary>
    private async Task<SalesOrder?> ValidateOrderLinkAsync(int? orderId, int customerId, int currencyId,
        IReadOnlyCollection<(int? OrderLineId, int ItemId)> lines, CancellationToken ct)
    {
        if (orderId is null)
        {
            if (lines.Any(l => l.OrderLineId is not null))
                throw new BusinessRuleException("A line can reference an order line only when the document is linked to a sales order.");
            return null;
        }

        var so = await db.SalesOrders.AsNoTracking().Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == orderId, ct)
                 ?? throw new BusinessRuleException("The sales order does not exist.");
        if (so.Status != DocumentStatus.Confirmed) throw new BusinessRuleException("Only a confirmed sales order can be delivered or invoiced.");
        if (so.CustomerId != customerId) throw new BusinessRuleException("The customer differs from the sales order's customer.");
        if (so.CurrencyId != currencyId) throw new BusinessRuleException("The currency differs from the sales order's currency.");

        var used = new HashSet<int>();
        foreach (var (lineId, itemId) in lines)
        {
            if (lineId is null) throw new BusinessRuleException("Every line must reference a sales order line when the document is linked to an order.");
            var ol = so.Lines.FirstOrDefault(x => x.Id == lineId) ?? throw new BusinessRuleException("An order line does not belong to the sales order.");
            if (ol.ItemId != itemId) throw new BusinessRuleException("A line's item does not match its sales order line.");
            if (!used.Add(ol.Id)) throw new BusinessRuleException("A sales order line may appear only once per document.");
        }
        return so;
    }

    /// <summary>What the customer owes right now, local currency: confirmed invoices minus confirmed returns and receipts.</summary>
    private async Task<decimal> CustomerBalanceAsync(int customerId, CancellationToken ct)
    {
        var invoiced = await db.SalesInvoices.AsNoTracking().Where(i => i.CustomerId == customerId && i.Status == DocumentStatus.Confirmed)
            .SumAsync(i => (decimal?)Math.Round(i.TotalAmount * i.ExchangeRate, 4), ct) ?? 0m;
        var returned = await db.SalesReturns.AsNoTracking().Where(r => r.CustomerId == customerId && r.Status == DocumentStatus.Confirmed)
            .SumAsync(r => (decimal?)Math.Round(r.TotalAmount * r.ExchangeRate, 4), ct) ?? 0m;
        var received = await db.CustomerReceipts.AsNoTracking().Where(r => r.CustomerId == customerId && r.Status == DocumentStatus.Confirmed)
            .SumAsync(r => (decimal?)Math.Round(r.Amount * r.ExchangeRate, 4), ct) ?? 0m;
        return invoiced - returned - received;
    }

    // =====================================================================
    //  Sales orders
    // =====================================================================

    public async Task<PagedResult<SalesOrderRow>> ListOrdersAsync(SalesFilter f, CancellationToken ct)
    {
        var q = db.SalesOrders.AsNoTracking().AsQueryable();
        if (f.Status is { } st) q = q.Where(o => o.Status == st);
        if (f.CustomerId is { } cid) q = q.Where(o => o.CustomerId == cid);
        if (f.PendingOnly) q = q.Where(o => o.Status == DocumentStatus.Confirmed && o.Lines.Any(l => l.DeliveredQuantity < l.Quantity));
        if (!string.IsNullOrWhiteSpace(f.Q)) { var s = Like(f.Q); q = q.Where(o => (o.Number != null && o.Number.Contains(s)) || (o.Notes != null && o.Notes.Contains(s))); }

        var (page, size) = Paging(f.Page, f.PageSize);
        var total = await q.CountAsync(ct);
        var raw = await q.OrderByDescending(o => o.Date).ThenByDescending(o => o.Id).Skip((page - 1) * size).Take(size)
            .Select(o => new
            {
                o.Id, o.Number, o.Date, o.RequiredDate, o.CustomerId, o.CurrencyId, o.Status, o.TotalAmount,
                AnyDelivered = o.Lines.Any(l => l.DeliveredQuantity > 0), AllDelivered = o.Lines.All(l => l.DeliveredQuantity >= l.Quantity),
                AnyInvoiced = o.Lines.Any(l => l.InvoicedQuantity > 0), AllInvoiced = o.Lines.All(l => l.InvoicedQuantity >= l.Quantity),
                Count = o.Lines.Count,
            }).ToListAsync(ct);

        var rows = raw.Select(o => new SalesOrderRow(o.Id, o.Number, o.Date, o.RequiredDate, o.CustomerId, o.CurrencyId, o.Status, o.TotalAmount,
            State(o.AnyDelivered, o.AllDelivered), State(o.AnyInvoiced, o.AllInvoiced), o.Count)).ToList();
        return new PagedResult<SalesOrderRow>(rows, total, page, size);
    }

    public async Task<SalesOrder> GetOrderAsync(int id, CancellationToken ct) =>
        await db.SalesOrders.AsNoTracking().Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == id, ct)
        ?? throw new NotFoundException("Sales order not found.");

    private async Task<SalesOrder> LoadOrderAsync(int id, CancellationToken ct) =>
        await db.SalesOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == id, ct)
        ?? throw new NotFoundException("Sales order not found.");

    private static List<SalesOrderLine> OrderLines(IEnumerable<SoLineInput> lines) =>
        lines.Select(l => new SalesOrderLine { ItemId = l.ItemId, Quantity = l.Quantity, UnitPrice = l.UnitPrice, DiscountPercent = l.DiscountPercent }).ToList();

    public async Task<SalesOrder> CreateOrderAsync(SalesOrderInput i, CancellationToken ct)
    {
        await EnsureCustomerAsync(i.CustomerId, ct);
        await db.EnsureItemsAsync(i.Lines.Select(l => l.ItemId), ct);
        var rate = await db.ResolveRateAsync(i.CurrencyId, i.Date, i.ExchangeRate, null, ct);

        var so = new SalesOrder
        {
            Date = i.Date, RequiredDate = i.RequiredDate, CustomerId = i.CustomerId, CurrencyId = i.CurrencyId, ExchangeRate = rate,
            Notes = Clean(i.Notes), Status = DocumentStatus.Draft, Lines = OrderLines(i.Lines),
            TotalAmount = i.Lines.Sum(l => LineNet(l.Quantity, l.UnitPrice, l.DiscountPercent)),
        };
        db.SalesOrders.Add(so);
        await SaveAsync(ct);
        return so;
    }

    public async Task<SalesOrder> UpdateOrderAsync(int id, SalesOrderInput i, CancellationToken ct)
    {
        var so = await LoadOrderAsync(id, ct);
        EnsureDraft(so.Status);
        await EnsureCustomerAsync(i.CustomerId, ct);
        await db.EnsureItemsAsync(i.Lines.Select(l => l.ItemId), ct);
        var rate = await db.ResolveRateAsync(i.CurrencyId, i.Date, i.ExchangeRate, so.CurrencyId == i.CurrencyId ? so.ExchangeRate : null, ct);

        so.Date = i.Date; so.RequiredDate = i.RequiredDate; so.CustomerId = i.CustomerId; so.CurrencyId = i.CurrencyId;
        so.ExchangeRate = rate; so.Notes = Clean(i.Notes);
        db.SalesOrderLines.RemoveRange(so.Lines);
        so.Lines = OrderLines(i.Lines);
        so.TotalAmount = i.Lines.Sum(l => LineNet(l.Quantity, l.UnitPrice, l.DiscountPercent));
        await SaveAsync(ct);
        return so;
    }

    public async Task<SalesOrder> ConfirmOrderAsync(int id, CancellationToken ct)
    {
        var so = await LoadOrderAsync(id, ct);
        EnsureDraft(so.Status);
        so.Number = await numbers.NextAsync(SequenceKeys.SalesOrder, ct);
        await EnsureCustomerAsync(so.CustomerId, ct);
        await db.EnsureItemsAsync(so.Lines.Select(l => l.ItemId), ct);
        so.Status = DocumentStatus.Confirmed;
        so.ConfirmedAt = DateTime.UtcNow;
        so.ConfirmedBy = user.UserName;
        await SaveAsync(ct);
        return so;
    }

    public async Task<SalesOrder> CancelOrderAsync(int id, CancellationToken ct)
    {
        var so = await LoadOrderAsync(id, ct);
        if (so.Status == DocumentStatus.Cancelled) throw new BusinessRuleException("The document is already cancelled.");
        if (so.Lines.Any(l => l.DeliveredQuantity > 0 || l.InvoicedQuantity > 0))
            throw new BusinessRuleException("This order already has confirmed deliveries or invoices. Cancel those first.");
        so.Status = DocumentStatus.Cancelled;
        await SaveAsync(ct);
        return so;
    }

    // =====================================================================
    //  Delivery notes (stock out)
    // =====================================================================

    public async Task<PagedResult<DeliveryNoteRow>> ListDeliveriesAsync(SalesFilter f, CancellationToken ct)
    {
        var q = db.DeliveryNotes.AsNoTracking().AsQueryable();
        if (f.Status is { } st) q = q.Where(d => d.Status == st);
        if (f.CustomerId is { } cid) q = q.Where(d => d.CustomerId == cid);
        if (f.UnassignedOnly)
            // Available = not cancelled (a failed stop cancels its delivery), not on a live trip, and not already handed over on a completed one.
            q = q.Where(d => d.Status != DocumentStatus.Cancelled && !db.TripStops.Any(s => s.DeliveryNoteId == d.Id &&
                db.DistributionTrips.Any(t => t.Id == s.TripId &&
                    (t.Status == TripStatus.Planned || t.Status == TripStatus.InTransit ||
                     (t.Status == TripStatus.Completed && s.Status == StopStatus.Delivered)))));
        if (!string.IsNullOrWhiteSpace(f.Q)) { var s = Like(f.Q); q = q.Where(d => (d.Number != null && d.Number.Contains(s)) || (d.Notes != null && d.Notes.Contains(s))); }

        var (page, size) = Paging(f.Page, f.PageSize);
        var total = await q.CountAsync(ct);
        var raw = await q.OrderByDescending(d => d.Date).ThenByDescending(d => d.Id).Skip((page - 1) * size).Take(size)
            .Select(d => new
            {
                d.Id, d.Number, d.Date, d.CustomerId, d.WarehouseId, d.SalesOrderId, d.CurrencyId, d.Status, d.TotalAmount, Count = d.Lines.Count,
                TripNumber = (from s in db.TripStops
                              join t in db.DistributionTrips on s.TripId equals t.Id
                              where s.DeliveryNoteId == d.Id && (t.Status == TripStatus.Planned || t.Status == TripStatus.InTransit)
                              select t.Number).FirstOrDefault(),
            }).ToListAsync(ct);

        var rows = raw.Select(d => new DeliveryNoteRow(d.Id, d.Number, d.Date, d.CustomerId, d.WarehouseId, d.SalesOrderId, d.CurrencyId,
            d.Status, d.TotalAmount, d.Count, d.TripNumber)).ToList();
        return new PagedResult<DeliveryNoteRow>(rows, total, page, size);
    }

    public async Task<DeliveryNote> GetDeliveryAsync(int id, CancellationToken ct) =>
        await db.DeliveryNotes.AsNoTracking().Include(d => d.Lines).FirstOrDefaultAsync(d => d.Id == id, ct)
        ?? throw new NotFoundException("Delivery note not found.");

    private async Task<DeliveryNote> LoadDeliveryAsync(int id, CancellationToken ct) =>
        await db.DeliveryNotes.Include(d => d.Lines).FirstOrDefaultAsync(d => d.Id == id, ct)
        ?? throw new NotFoundException("Delivery note not found.");

    private static List<DeliveryNoteLine> DeliveryLines(IEnumerable<DeliveryLineInput> lines) =>
        lines.Select(l => new DeliveryNoteLine { SalesOrderLineId = l.SalesOrderLineId, ItemId = l.ItemId, Quantity = l.Quantity, UnitPrice = l.UnitPrice }).ToList();

    private async Task ValidateDeliveryAsync(DeliveryNoteInput i, CancellationToken ct)
    {
        await EnsureCustomerAsync(i.CustomerId, ct);
        await db.EnsureWarehouseAsync(i.WarehouseId, ct);
        await db.EnsureItemsAsync(i.Lines.Select(l => l.ItemId), ct);
        await ValidateOrderLinkAsync(i.SalesOrderId, i.CustomerId, i.CurrencyId, i.Lines.Select(l => (l.SalesOrderLineId, l.ItemId)).ToList(), ct);
    }

    public async Task<DeliveryNote> CreateDeliveryAsync(DeliveryNoteInput i, CancellationToken ct)
    {
        await ValidateDeliveryAsync(i, ct);
        var order = i.SalesOrderId is { } oid ? await db.SalesOrders.AsNoTracking().FirstAsync(o => o.Id == oid, ct) : null;
        var rate = await db.ResolveRateAsync(i.CurrencyId, i.Date, i.ExchangeRate, order?.ExchangeRate, ct);

        var d = new DeliveryNote
        {
            Date = i.Date, CustomerId = i.CustomerId, WarehouseId = i.WarehouseId, SalesOrderId = i.SalesOrderId, CurrencyId = i.CurrencyId,
            ExchangeRate = rate, Notes = Clean(i.Notes), Status = DocumentStatus.Draft, Lines = DeliveryLines(i.Lines),
            TotalAmount = i.Lines.Sum(l => R4(l.Quantity * l.UnitPrice)),
        };
        db.DeliveryNotes.Add(d);
        await SaveAsync(ct);
        return d;
    }

    public async Task<DeliveryNote> UpdateDeliveryAsync(int id, DeliveryNoteInput i, CancellationToken ct)
    {
        var d = await LoadDeliveryAsync(id, ct);
        EnsureDraft(d.Status);
        await ValidateDeliveryAsync(i, ct);
        var order = i.SalesOrderId is { } oid ? await db.SalesOrders.AsNoTracking().FirstAsync(o => o.Id == oid, ct) : null;
        var rate = await db.ResolveRateAsync(i.CurrencyId, i.Date, i.ExchangeRate,
            order?.ExchangeRate ?? (d.CurrencyId == i.CurrencyId ? d.ExchangeRate : null), ct);

        d.Date = i.Date; d.CustomerId = i.CustomerId; d.WarehouseId = i.WarehouseId; d.SalesOrderId = i.SalesOrderId;
        d.CurrencyId = i.CurrencyId; d.ExchangeRate = rate; d.Notes = Clean(i.Notes);
        db.DeliveryNoteLines.RemoveRange(d.Lines);
        d.Lines = DeliveryLines(i.Lines);
        d.TotalAmount = i.Lines.Sum(l => R4(l.Quantity * l.UnitPrice));
        await SaveAsync(ct);
        return d;
    }

    public async Task<DeliveryNote> ConfirmDeliveryAsync(int id, CancellationToken ct)
    {
        var d = await LoadDeliveryAsync(id, ct);
        EnsureDraft(d.Status);
        d.Number = await numbers.NextAsync(SequenceKeys.Delivery, ct);
        await EnsureCustomerAsync(d.CustomerId, ct);
        await db.EnsureWarehouseAsync(d.WarehouseId, ct);
        await db.EnsureItemsAsync(d.Lines.Select(l => l.ItemId), ct);

        SalesOrder? so = null;
        if (d.SalesOrderId is { } oid)
        {
            so = await LoadOrderAsync(oid, ct);
            if (so.Status != DocumentStatus.Confirmed) throw new BusinessRuleException("The linked sales order is no longer confirmed.");
        }

        var src = new PostingSource(d.Date, DocumentTypes.Delivery, d.Id, d.Number);
        var costOfGoods = 0m;
        foreach (var line in d.Lines)
        {
            if (line.SalesOrderLineId is { } olid)
            {
                var ol = so?.Lines.FirstOrDefault(x => x.Id == olid) ?? throw new BusinessRuleException("An order line does not belong to the sales order.");
                var remaining = ol.Quantity - ol.DeliveredQuantity;
                if (line.Quantity > remaining)
                {
                    var code = await db.Items.Where(x => x.Id == line.ItemId).Select(x => x.Code).FirstAsync(ct);
                    throw new BusinessRuleException($"Delivery quantity for item {code} exceeds the remaining ordered quantity ({remaining:0.####}).");
                }
                ol.DeliveredQuantity += line.Quantity;
            }
            // Stock leaves at the warehouse's current average cost; remember it as the cost of goods sold.
            line.UnitCost = await poster.IssueAsync(src, line.ItemId, d.WarehouseId, line.Quantity, MovementType.SalesDelivery, null, ct);
            costOfGoods += R4(line.Quantity * line.UnitCost.Value);
        }

        await gl.PostAsync(SourceTypes.DeliveryNote, d.Id, d.Date, $"Delivery {d.Number}", ct,
            new PostLine(AccountRoles.CostOfGoodsSold, costOfGoods, 0),
            new PostLine(AccountRoles.Inventory, 0, costOfGoods));

        d.Status = DocumentStatus.Confirmed;
        d.ConfirmedAt = DateTime.UtcNow;
        d.ConfirmedBy = user.UserName;
        await SaveAsync(ct);
        return d;
    }

    public async Task<DeliveryNote> CancelDeliveryAsync(int id, CancellationToken ct)
    {
        var d = await LoadDeliveryAsync(id, ct);
        await CancelDeliveryCoreAsync(d, true, ct);
        await SaveAsync(ct);
        return d;
    }

    /// <summary>Stages the cancellation (stock back, order quantities, GL reversal) without saving. A trip that has already left blocks it unless the trip itself is cancelling a failed stop.</summary>
    private async Task CancelDeliveryCoreAsync(DeliveryNote d, bool checkTrip, CancellationToken ct)
    {
        if (d.Status == DocumentStatus.Cancelled) throw new BusinessRuleException("The document is already cancelled.");

        var onActiveTrip = !checkTrip ? null : await (from s in db.TripStops
                                  join t in db.DistributionTrips on s.TripId equals t.Id
                                  where s.DeliveryNoteId == d.Id && (t.Status == TripStatus.InTransit || t.Status == TripStatus.Completed)
                                  select t.Number).FirstOrDefaultAsync(ct);
        if (onActiveTrip is not null)
            throw new BusinessRuleException($"This delivery is on trip {onActiveTrip}, which has already left. Cancel or fix the trip first.");

        if (d.Status == DocumentStatus.Confirmed)
        {
            if (d.SalesOrderId is { } oid)
            {
                var so = await LoadOrderAsync(oid, ct);
                foreach (var line in d.Lines.Where(l => l.SalesOrderLineId is not null))
                {
                    var ol = so.Lines.First(x => x.Id == line.SalesOrderLineId);
                    if (ol.DeliveredQuantity - line.Quantity < ol.InvoicedQuantity)
                        throw new BusinessRuleException("Invoices have already been confirmed against this delivery's quantities. Cancel them first.");
                    ol.DeliveredQuantity -= line.Quantity;
                }
            }
            await poster.ReverseDocumentAsync(DocumentTypes.Delivery, d.Id, d.Number, ct);
            await gl.ReverseAsync(SourceTypes.DeliveryNote, d.Id, $"Cancelled delivery {d.Number}", ct);
        }
        d.Status = DocumentStatus.Cancelled;
    }

    // =====================================================================
    //  Sales invoices
    // =====================================================================

    public async Task<PagedResult<SalesInvoiceRow>> ListInvoicesAsync(SalesFilter f, CancellationToken ct)
    {
        var q = db.SalesInvoices.AsNoTracking().AsQueryable();
        if (f.Status is { } st) q = q.Where(i => i.Status == st);
        if (f.CustomerId is { } cid) q = q.Where(i => i.CustomerId == cid);
        if (!string.IsNullOrWhiteSpace(f.Q)) { var s = Like(f.Q); q = q.Where(i => i.Number != null && i.Number.Contains(s)); }

        var (page, size) = Paging(f.Page, f.PageSize);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(i => i.Date).ThenByDescending(i => i.Id).Skip((page - 1) * size).Take(size)
            .Select(i => new SalesInvoiceRow(i.Id, i.Number, i.Date, i.DueDate, i.CustomerId, i.SalesOrderId, i.CurrencyId, i.Status, i.TotalAmount))
            .ToListAsync(ct);
        return new PagedResult<SalesInvoiceRow>(rows, total, page, size);
    }

    public async Task<SalesInvoice> GetInvoiceAsync(int id, CancellationToken ct) =>
        await db.SalesInvoices.AsNoTracking().Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id, ct)
        ?? throw new NotFoundException("Sales invoice not found.");

    private async Task<SalesInvoice> LoadInvoiceAsync(int id, CancellationToken ct) =>
        await db.SalesInvoices.Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id, ct)
        ?? throw new NotFoundException("Sales invoice not found.");

    private static List<SalesInvoiceLine> InvoiceLines(IEnumerable<SalesInvoiceLineInput> lines) =>
        lines.Select(l => new SalesInvoiceLine { SalesOrderLineId = l.SalesOrderLineId, ItemId = l.ItemId, Quantity = l.Quantity, UnitPrice = l.UnitPrice, DiscountPercent = l.DiscountPercent }).ToList();

    /// <summary>Sum of the discounted lines minus the invoice-level discount, which may not exceed that sum.</summary>
    private static decimal InvoiceTotal(SalesInvoiceInput i)
    {
        var subtotal = i.Lines.Sum(l => LineNet(l.Quantity, l.UnitPrice, l.DiscountPercent));
        if (R4(i.DiscountAmount) > subtotal) throw new BusinessRuleException("The invoice discount cannot exceed the sum of the lines.");
        return subtotal - R4(i.DiscountAmount);
    }

    private async Task ValidateInvoiceAsync(SalesInvoiceInput i, CancellationToken ct)
    {
        await EnsureCustomerAsync(i.CustomerId, ct);
        await db.EnsureItemsAsync(i.Lines.Select(l => l.ItemId), ct);
        await ValidateOrderLinkAsync(i.SalesOrderId, i.CustomerId, i.CurrencyId, i.Lines.Select(l => (l.SalesOrderLineId, l.ItemId)).ToList(), ct);
    }

    public async Task<SalesInvoice> CreateInvoiceAsync(SalesInvoiceInput i, CancellationToken ct)
    {
        await ValidateInvoiceAsync(i, ct);
        var order = i.SalesOrderId is { } oid ? await db.SalesOrders.AsNoTracking().FirstAsync(o => o.Id == oid, ct) : null;
        var rate = await db.ResolveRateAsync(i.CurrencyId, i.Date, i.ExchangeRate, order?.ExchangeRate, ct);

        var inv = new SalesInvoice
        {
            Date = i.Date, DueDate = i.DueDate, CustomerId = i.CustomerId, SalesOrderId = i.SalesOrderId, CurrencyId = i.CurrencyId,
            ExchangeRate = rate, Notes = Clean(i.Notes), Status = DocumentStatus.Draft, Lines = InvoiceLines(i.Lines),
            DiscountAmount = R4(i.DiscountAmount), TotalAmount = InvoiceTotal(i),
        };
        db.SalesInvoices.Add(inv);
        await SaveAsync(ct);
        return inv;
    }

    public async Task<SalesInvoice> UpdateInvoiceAsync(int id, SalesInvoiceInput i, CancellationToken ct)
    {
        var inv = await LoadInvoiceAsync(id, ct);
        EnsureDraft(inv.Status);
        await ValidateInvoiceAsync(i, ct);
        var order = i.SalesOrderId is { } oid ? await db.SalesOrders.AsNoTracking().FirstAsync(o => o.Id == oid, ct) : null;
        var rate = await db.ResolveRateAsync(i.CurrencyId, i.Date, i.ExchangeRate,
            order?.ExchangeRate ?? (inv.CurrencyId == i.CurrencyId ? inv.ExchangeRate : null), ct);

        inv.Date = i.Date; inv.DueDate = i.DueDate; inv.CustomerId = i.CustomerId; inv.SalesOrderId = i.SalesOrderId;
        inv.CurrencyId = i.CurrencyId; inv.ExchangeRate = rate; inv.Notes = Clean(i.Notes);
        db.SalesInvoiceLines.RemoveRange(inv.Lines);
        inv.Lines = InvoiceLines(i.Lines);
        inv.DiscountAmount = R4(i.DiscountAmount);
        inv.TotalAmount = InvoiceTotal(i);
        await SaveAsync(ct);
        return inv;
    }

    public async Task<SalesInvoice> ConfirmInvoiceAsync(int id, CancellationToken ct)
    {
        var inv = await LoadInvoiceAsync(id, ct);
        EnsureDraft(inv.Status);
        inv.Number = await numbers.NextAsync(SequenceKeys.SalesInvoice, ct);
        await EnsureCustomerAsync(inv.CustomerId, ct);
        await db.EnsureItemsAsync(inv.Lines.Select(l => l.ItemId), ct);

        if (inv.SalesOrderId is { } oid)
        {
            var so = await LoadOrderAsync(oid, ct);
            if (so.Status != DocumentStatus.Confirmed) throw new BusinessRuleException("The linked sales order is no longer confirmed.");
            foreach (var line in inv.Lines)
            {
                var ol = so.Lines.FirstOrDefault(x => x.Id == line.SalesOrderLineId) ?? throw new BusinessRuleException("An order line does not belong to the sales order.");
                // Bill for goods that have left: invoiced quantity may not run ahead of delivered quantity.
                var billable = ol.DeliveredQuantity - ol.InvoicedQuantity;
                if (line.Quantity > billable)
                {
                    var code = await db.Items.Where(x => x.Id == line.ItemId).Select(x => x.Code).FirstAsync(ct);
                    throw new BusinessRuleException($"Invoice quantity for item {code} exceeds the delivered, not-yet-invoiced quantity ({billable:0.####}).");
                }
                ol.InvoicedQuantity += line.Quantity;
            }
        }

        // Credit limit (0 = none): the new invoice may not push the balance over it.
        var customer = await db.Customers.AsNoTracking().FirstAsync(c => c.Id == inv.CustomerId, ct);
        if (customer.CreditLimit > 0)
        {
            var balance = await CustomerBalanceAsync(customer.Id, ct);
            var thisInvoice = R4(inv.TotalAmount * inv.ExchangeRate);
            if (balance + thisInvoice > customer.CreditLimit)
                throw new BusinessRuleException(
                    $"Credit limit exceeded: limit {customer.CreditLimit:0.##}, current balance {balance:0.##}, this invoice {thisInvoice:0.##} (local currency).");
        }

        // Revenue is booked at the undiscounted value and the discounts given go to their own contra account, so the books show how much was discounted.
        var receivable = R4(inv.TotalAmount * inv.ExchangeRate);
        var revenue = R4(inv.Lines.Sum(l => LineGross(l.Quantity, l.UnitPrice)) * inv.ExchangeRate);
        var discounts = revenue - receivable;
        await gl.PostAsync(SourceTypes.SalesInvoice, inv.Id, inv.Date, $"Sales invoice {inv.Number}", ct,
            new PostLine(AccountRoles.Receivables, receivable, 0, PartyType.Customer, inv.CustomerId),
            new PostLine(AccountRoles.SalesDiscounts, discounts, 0),
            new PostLine(AccountRoles.SalesRevenue, 0, revenue));

        inv.Status = DocumentStatus.Confirmed;
        inv.ConfirmedAt = DateTime.UtcNow;
        inv.ConfirmedBy = user.UserName;
        await SaveAsync(ct);
        return inv;
    }

    public async Task<SalesInvoice> CancelInvoiceAsync(int id, CancellationToken ct)
    {
        var inv = await LoadInvoiceAsync(id, ct);
        if (inv.Status == DocumentStatus.Cancelled) throw new BusinessRuleException("The document is already cancelled.");
        if (inv.Status == DocumentStatus.Confirmed && await db.CustomerReceiptAllocations.AnyAsync(a => a.SalesInvoiceId == id &&
                db.CustomerReceipts.Any(r => r.Id == a.CustomerReceiptId && r.Status == DocumentStatus.Confirmed), ct))
            throw new BusinessRuleException("Payments are already allocated to this invoice. Cancel them first.");
        if (inv.Status == DocumentStatus.Confirmed && await db.SalesReturns.AnyAsync(r => r.SalesInvoiceId == id && r.Status == DocumentStatus.Confirmed, ct))
            throw new BusinessRuleException("Returns are already linked to this invoice. Cancel them first.");

        if (inv.Status == DocumentStatus.Confirmed && inv.SalesOrderId is { } oid)
        {
            var so = await LoadOrderAsync(oid, ct);
            foreach (var line in inv.Lines)
                so.Lines.First(x => x.Id == line.SalesOrderLineId).InvoicedQuantity -= line.Quantity;
        }
        if (inv.Status == DocumentStatus.Confirmed) await gl.ReverseAsync(SourceTypes.SalesInvoice, inv.Id, $"Cancelled sales invoice {inv.Number}", ct);
        inv.Status = DocumentStatus.Cancelled;
        await SaveAsync(ct);
        return inv;
    }

    // =====================================================================
    //  Sales returns (stock in, credit to the customer)
    // =====================================================================

    public async Task<PagedResult<SalesReturnRow>> ListReturnsAsync(SalesFilter f, CancellationToken ct)
    {
        var q = db.SalesReturns.AsNoTracking().AsQueryable();
        if (f.Status is { } st) q = q.Where(r => r.Status == st);
        if (f.CustomerId is { } cid) q = q.Where(r => r.CustomerId == cid);
        if (!string.IsNullOrWhiteSpace(f.Q)) { var s = Like(f.Q); q = q.Where(r => (r.Number != null && r.Number.Contains(s)) || (r.Notes != null && r.Notes.Contains(s))); }

        var (page, size) = Paging(f.Page, f.PageSize);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(r => r.Date).ThenByDescending(r => r.Id).Skip((page - 1) * size).Take(size)
            .Select(r => new SalesReturnRow(r.Id, r.Number, r.Date, r.CustomerId, r.WarehouseId, r.SalesInvoiceId, r.CurrencyId, r.Status, r.TotalAmount, r.Lines.Count))
            .ToListAsync(ct);
        return new PagedResult<SalesReturnRow>(rows, total, page, size);
    }

    public async Task<SalesReturn> GetReturnAsync(int id, CancellationToken ct) =>
        await db.SalesReturns.AsNoTracking().Include(r => r.Lines).FirstOrDefaultAsync(r => r.Id == id, ct)
        ?? throw new NotFoundException("Sales return not found.");

    private async Task<SalesReturn> LoadReturnAsync(int id, CancellationToken ct) =>
        await db.SalesReturns.Include(r => r.Lines).FirstOrDefaultAsync(r => r.Id == id, ct)
        ?? throw new NotFoundException("Sales return not found.");

    private static List<SalesReturnLine> ReturnLines(IEnumerable<ReturnLineInput> lines) =>
        lines.Select(l => new SalesReturnLine { ItemId = l.ItemId, Quantity = l.Quantity, UnitPrice = l.UnitPrice }).ToList();

    /// <summary>A return linked to an invoice must match its customer and currency, use only items on it, and stay within what was invoiced and not yet returned.</summary>
    private async Task ValidateReturnAsync(SalesReturnInput i, int? returnId, CancellationToken ct)
    {
        await EnsureCustomerAsync(i.CustomerId, ct);
        await db.EnsureWarehouseAsync(i.WarehouseId, ct);
        await db.EnsureItemsAsync(i.Lines.Select(l => l.ItemId), ct);
        if (i.SalesInvoiceId is not { } invoiceId) return;

        var inv = await db.SalesInvoices.AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == invoiceId, ct)
                  ?? throw new BusinessRuleException("The sales invoice does not exist.");
        if (inv.Status != DocumentStatus.Confirmed) throw new BusinessRuleException("Only a confirmed invoice can be linked to a return.");
        if (inv.CustomerId != i.CustomerId) throw new BusinessRuleException("The customer differs from the invoice's customer.");
        if (inv.CurrencyId != i.CurrencyId) throw new BusinessRuleException("The currency differs from the invoice's currency.");

        var returned = (await db.SalesReturnLines.AsNoTracking()
            .Where(l => db.SalesReturns.Any(r => r.Id == l.SalesReturnId && r.SalesInvoiceId == invoiceId && r.Status == DocumentStatus.Confirmed && (returnId == null || r.Id != returnId)))
            .GroupBy(l => l.ItemId).Select(g => new { Item = g.Key, Qty = g.Sum(x => x.Quantity) }).ToListAsync(ct))
            .ToDictionary(x => x.Item, x => x.Qty);

        foreach (var line in i.Lines)
        {
            var invoiced = inv.Lines.Where(l => l.ItemId == line.ItemId).Sum(l => l.Quantity);
            var code = await db.Items.Where(x => x.Id == line.ItemId).Select(x => x.Code).FirstAsync(ct);
            if (invoiced == 0) throw new BusinessRuleException($"Item {code} is not on the linked invoice.");
            var remaining = invoiced - returned.GetValueOrDefault(line.ItemId);
            if (line.Quantity > remaining)
                throw new BusinessRuleException($"Returned quantity for item {code} exceeds what was invoiced and not yet returned ({remaining:0.####}).");
        }
    }

    private static SalesReturnInput AsInput(SalesReturn r) =>
        new(r.Date, r.CustomerId, r.WarehouseId, r.CurrencyId, r.ExchangeRate, r.Notes,
            r.Lines.Select(l => new ReturnLineInput(l.ItemId, l.Quantity, l.UnitPrice)).ToList(), r.SalesInvoiceId);

    public async Task<SalesReturn> CreateReturnAsync(SalesReturnInput i, CancellationToken ct)
    {
        await ValidateReturnAsync(i, null, ct);
        var rate = await db.ResolveRateAsync(i.CurrencyId, i.Date, i.ExchangeRate, null, ct);

        var r = new SalesReturn
        {
            Date = i.Date, CustomerId = i.CustomerId, WarehouseId = i.WarehouseId, SalesInvoiceId = i.SalesInvoiceId, CurrencyId = i.CurrencyId, ExchangeRate = rate,
            Notes = Clean(i.Notes), Status = DocumentStatus.Draft, Lines = ReturnLines(i.Lines),
            TotalAmount = i.Lines.Sum(l => R4(l.Quantity * l.UnitPrice)),
        };
        db.SalesReturns.Add(r);
        await SaveAsync(ct);
        return r;
    }

    public async Task<SalesReturn> UpdateReturnAsync(int id, SalesReturnInput i, CancellationToken ct)
    {
        var r = await LoadReturnAsync(id, ct);
        EnsureDraft(r.Status);
        await ValidateReturnAsync(i, id, ct);
        var rate = await db.ResolveRateAsync(i.CurrencyId, i.Date, i.ExchangeRate, r.CurrencyId == i.CurrencyId ? r.ExchangeRate : null, ct);

        r.Date = i.Date; r.CustomerId = i.CustomerId; r.WarehouseId = i.WarehouseId; r.SalesInvoiceId = i.SalesInvoiceId; r.CurrencyId = i.CurrencyId;
        r.ExchangeRate = rate; r.Notes = Clean(i.Notes);
        db.SalesReturnLines.RemoveRange(r.Lines);
        r.Lines = ReturnLines(i.Lines);
        r.TotalAmount = i.Lines.Sum(l => R4(l.Quantity * l.UnitPrice));
        await SaveAsync(ct);
        return r;
    }

    public async Task<SalesReturn> ConfirmReturnAsync(int id, CancellationToken ct)
    {
        var r = await LoadReturnAsync(id, ct);
        EnsureDraft(r.Status);
        r.Number = await numbers.NextAsync(SequenceKeys.SalesReturn, ct);
        await ValidateReturnAsync(AsInput(r), r.Id, ct);

        var src = new PostingSource(r.Date, DocumentTypes.SalesReturn, r.Id, r.Number);
        var costBack = 0m;
        foreach (var line in r.Lines)
        {
            // Goods re-enter stock at the warehouse's current average cost (else the item's standard cost), so the average is not distorted.
            var balance = await poster.BalanceAsync(line.ItemId, r.WarehouseId, ct);
            var cost = balance.AverageCost > 0 ? balance.AverageCost
                : await db.Items.Where(x => x.Id == line.ItemId).Select(x => x.CostPrice).FirstAsync(ct);
            line.UnitCost = cost;
            await poster.ReceiveAsync(src, line.ItemId, r.WarehouseId, line.Quantity, cost, MovementType.SalesReturn, null, ct);
            costBack += R4(line.Quantity * cost);
        }

        var credit = R4(r.TotalAmount * r.ExchangeRate);
        await gl.PostAsync(SourceTypes.SalesReturn, r.Id, r.Date, $"Sales return {r.Number}", ct,
            new PostLine(AccountRoles.SalesReturns, credit, 0),
            new PostLine(AccountRoles.Receivables, 0, credit, PartyType.Customer, r.CustomerId),
            new PostLine(AccountRoles.Inventory, costBack, 0),
            new PostLine(AccountRoles.CostOfGoodsSold, 0, costBack));

        r.Status = DocumentStatus.Confirmed;
        r.ConfirmedAt = DateTime.UtcNow;
        r.ConfirmedBy = user.UserName;
        await SaveAsync(ct);
        return r;
    }

    public async Task<SalesReturn> CancelReturnAsync(int id, CancellationToken ct)
    {
        var r = await LoadReturnAsync(id, ct);
        if (r.Status == DocumentStatus.Cancelled) throw new BusinessRuleException("The document is already cancelled.");
        if (r.Status == DocumentStatus.Confirmed)
        {
            await poster.ReverseDocumentAsync(DocumentTypes.SalesReturn, r.Id, r.Number, ct);
            await gl.ReverseAsync(SourceTypes.SalesReturn, r.Id, $"Cancelled sales return {r.Number}", ct);
        }
        r.Status = DocumentStatus.Cancelled;
        await SaveAsync(ct);
        return r;
    }

    // =====================================================================
    //  Customer receipts (optionally allocated to invoices)
    // =====================================================================

    /// <summary>Amount of a sales invoice already settled by CONFIRMED receipts (optionally ignoring one receipt).</summary>
    private async Task<decimal> SettledAsync(int invoiceId, int? exceptReceiptId, CancellationToken ct)
    {
        var paid = await db.CustomerReceiptAllocations.AsNoTracking()
            .Where(a => a.SalesInvoiceId == invoiceId && db.CustomerReceipts.Any(r =>
                r.Id == a.CustomerReceiptId && r.Status == DocumentStatus.Confirmed && (exceptReceiptId == null || r.Id != exceptReceiptId)))
            .SumAsync(a => (decimal?)a.Amount, ct) ?? 0m;
        // goods returned against the invoice reduce what is still owed on it
        return paid + (await db.SalesReturns.AsNoTracking().Where(r => r.SalesInvoiceId == invoiceId && r.Status == DocumentStatus.Confirmed).SumAsync(r => (decimal?)r.TotalAmount, ct) ?? 0m);
    }

    private async Task ValidateAllocationsAsync(int customerId, int currencyId, decimal amount, List<AllocationInput>? allocations, int? receiptId, CancellationToken ct)
    {
        if (allocations is null or { Count: 0 }) return;
        if (allocations.Any(a => a.Kind != InvoiceKinds.SalesInvoice)) throw new BusinessRuleException("A receipt can only be allocated to sales invoices.");
        if (allocations.Any(a => a.Amount <= 0)) throw new BusinessRuleException("Allocation amounts must be greater than zero.");
        if (allocations.Sum(a => a.Amount) > R4(amount)) throw new BusinessRuleException("The allocations exceed the payment amount.");
        if (allocations.GroupBy(a => a.InvoiceId).Any(g => g.Count() > 1)) throw new BusinessRuleException("An invoice may appear only once among the allocations.");

        foreach (var a in allocations)
        {
            var inv = await db.SalesInvoices.AsNoTracking().Where(i => i.Id == a.InvoiceId)
                          .Select(i => new { i.Number, i.CustomerId, i.CurrencyId, i.Status, i.TotalAmount }).FirstOrDefaultAsync(ct)
                      ?? throw new BusinessRuleException("An allocated invoice does not exist.");
            if (inv.Status != DocumentStatus.Confirmed) throw new BusinessRuleException($"Invoice {inv.Number} is not confirmed, so it cannot be paid yet.");
            if (inv.CustomerId != customerId) throw new BusinessRuleException($"Invoice {inv.Number} belongs to another customer.");
            if (inv.CurrencyId != currencyId) throw new BusinessRuleException($"Invoice {inv.Number} is in a different currency from the payment.");
            var outstanding = inv.TotalAmount - await SettledAsync(a.InvoiceId, receiptId, ct);
            if (R4(a.Amount) > outstanding)
                throw new BusinessRuleException($"The allocation to invoice {inv.Number} exceeds its outstanding amount ({outstanding:0.####}).");
        }
    }

    private static List<CustomerReceiptAllocation> ToAllocations(List<AllocationInput>? input) =>
        (input ?? []).Select(a => new CustomerReceiptAllocation { SalesInvoiceId = a.InvoiceId, Amount = R4(a.Amount) }).ToList();

    public async Task<PagedResult<CustomerReceipt>> ListReceiptsAsync(SalesFilter f, CancellationToken ct)
    {
        var q = db.CustomerReceipts.AsNoTracking().AsQueryable();
        if (f.Status is { } st) q = q.Where(r => r.Status == st);
        if (f.CustomerId is { } cid) q = q.Where(r => r.CustomerId == cid);
        if (!string.IsNullOrWhiteSpace(f.Q))
        {
            var s = Like(f.Q);
            q = q.Where(r => (r.Number != null && r.Number.Contains(s)) || (r.Reference != null && r.Reference.Contains(s)));
        }

        var (page, size) = Paging(f.Page, f.PageSize);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(r => r.Date).ThenByDescending(r => r.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new PagedResult<CustomerReceipt>(rows, total, page, size);
    }

    public async Task<CustomerReceipt> GetReceiptAsync(int id, CancellationToken ct) =>
        await db.CustomerReceipts.AsNoTracking().Include(r => r.Allocations).FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("Receipt not found.");

    private async Task<CustomerReceipt> LoadReceiptAsync(int id, CancellationToken ct) =>
        await db.CustomerReceipts.Include(r => r.Allocations).FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("Receipt not found.");

    public async Task<CustomerReceipt> CreateReceiptAsync(CustomerReceiptInput i, CancellationToken ct)
    {
        await EnsureCustomerAsync(i.CustomerId, ct);
        var rate = await db.ResolveRateAsync(i.CurrencyId, i.Date, i.ExchangeRate, null, ct);
        await ValidateAllocationsAsync(i.CustomerId, i.CurrencyId, i.Amount, i.Allocations, null, ct);
        var r = new CustomerReceipt
        {
            Date = i.Date, CustomerId = i.CustomerId, CurrencyId = i.CurrencyId, ExchangeRate = rate, Amount = R4(i.Amount),
            Method = i.Method, Reference = Clean(i.Reference), Notes = Clean(i.Notes), Status = DocumentStatus.Draft,
            Allocations = ToAllocations(i.Allocations),
        };
        db.CustomerReceipts.Add(r);
        await SaveAsync(ct);
        return r;
    }

    public async Task<CustomerReceipt> UpdateReceiptAsync(int id, CustomerReceiptInput i, CancellationToken ct)
    {
        var r = await LoadReceiptAsync(id, ct);
        EnsureDraft(r.Status);
        await EnsureCustomerAsync(i.CustomerId, ct);
        var rate = await db.ResolveRateAsync(i.CurrencyId, i.Date, i.ExchangeRate, r.CurrencyId == i.CurrencyId ? r.ExchangeRate : null, ct);
        await ValidateAllocationsAsync(i.CustomerId, i.CurrencyId, i.Amount, i.Allocations, id, ct);
        r.Date = i.Date; r.CustomerId = i.CustomerId; r.CurrencyId = i.CurrencyId; r.ExchangeRate = rate; r.Amount = R4(i.Amount);
        r.Method = i.Method; r.Reference = Clean(i.Reference); r.Notes = Clean(i.Notes);
        db.CustomerReceiptAllocations.RemoveRange(r.Allocations);
        r.Allocations = ToAllocations(i.Allocations);
        await SaveAsync(ct);
        return r;
    }

    public async Task<CustomerReceipt> ConfirmReceiptAsync(int id, CancellationToken ct)
    {
        var r = await LoadReceiptAsync(id, ct);
        EnsureDraft(r.Status);
        r.Number = await numbers.NextAsync(SequenceKeys.CustomerReceipt, ct);
        await EnsureCustomerAsync(r.CustomerId, ct);
        await ValidateAllocationsAsync(r.CustomerId, r.CurrencyId, r.Amount,
            r.Allocations.Select(a => new AllocationInput(InvoiceKinds.SalesInvoice, a.SalesInvoiceId, a.Amount)).ToList(), r.Id, ct);

        // The allocated part clears the receivable at the INVOICE's rate; the money actually received in local currency uses
        // THIS receipt's rate. The difference is a realised exchange gain / loss. The unallocated part stays on account.
        decimal settled = 0m, receivedAllocated = 0m, allocatedTotal = 0m;
        foreach (var a in r.Allocations)
        {
            var rate = await db.SalesInvoices.AsNoTracking().Where(i => i.Id == a.SalesInvoiceId).Select(i => i.ExchangeRate).FirstAsync(ct);
            settled += R4(a.Amount * rate);
            receivedAllocated += R4(a.Amount * r.ExchangeRate);
            allocatedTotal += a.Amount;
        }
        var onAccount = R4((r.Amount - allocatedTotal) * r.ExchangeRate);
        var receivedLocal = receivedAllocated + onAccount;
        var cleared = settled + onAccount;
        var fx = receivedAllocated - settled; // received more local currency than the receivable that was booked = gain

        r.LocalSettled = cleared;
        await gl.PostAsync(SourceTypes.CustomerReceipt, r.Id, r.Date, $"Customer receipt {r.Number}", ct,
            new PostLine(r.Method == PaymentMethod.Cash ? AccountRoles.Cash : AccountRoles.Bank, receivedLocal, 0),
            new PostLine(AccountRoles.ExchangeLoss, fx < 0 ? -fx : 0, 0),
            new PostLine(AccountRoles.Receivables, 0, cleared, PartyType.Customer, r.CustomerId),
            new PostLine(AccountRoles.ExchangeGain, 0, fx > 0 ? fx : 0));

        r.Status = DocumentStatus.Confirmed;
        r.ConfirmedAt = DateTime.UtcNow;
        r.ConfirmedBy = user.UserName;
        await SaveAsync(ct);
        return r;
    }

    public async Task<CustomerReceipt> CancelReceiptAsync(int id, CancellationToken ct)
    {
        var r = await LoadReceiptAsync(id, ct);
        if (r.Status == DocumentStatus.Cancelled) throw new BusinessRuleException("The document is already cancelled.");
        if (r.Status == DocumentStatus.Confirmed) await gl.ReverseAsync(SourceTypes.CustomerReceipt, r.Id, $"Cancelled customer receipt {r.Number}", ct);
        r.Status = DocumentStatus.Cancelled;
        await SaveAsync(ct);
        return r;
    }

    // =====================================================================
    //  Distribution trips
    // =====================================================================

    public async Task<PagedResult<TripRow>> ListTripsAsync(TripFilter f, CancellationToken ct)
    {
        var q = db.DistributionTrips.AsNoTracking().AsQueryable();
        if (f.Status is { } st) q = q.Where(t => t.Status == st);
        if (f.DriverId is { } did) q = q.Where(t => t.DriverId == did);
        if (f.VehicleId is { } vid) q = q.Where(t => t.VehicleId == vid);
        if (f.From is { } from) q = q.Where(t => t.Date >= from);
        if (f.To is { } to) q = q.Where(t => t.Date <= to);
        if (!string.IsNullOrWhiteSpace(f.Q))
        {
            var s = Like(f.Q);
            q = q.Where(t => (t.Number != null && t.Number.Contains(s)) || (t.Notes != null && t.Notes.Contains(s))
                             || db.Drivers.Any(d => d.Id == t.DriverId && d.Name.Contains(s)) || db.Vehicles.Any(v => v.Id == t.VehicleId && v.PlateNumber.Contains(s)));
        }

        var (page, size) = Paging(f.Page, f.PageSize);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(t => t.Date).ThenByDescending(t => t.Id).Skip((page - 1) * size).Take(size)
            .Select(t => new TripRow(t.Id, t.Number, t.Date, t.DriverId, t.VehicleId, t.Status, t.Stops.Count, t.Stops.Count(s => s.Status == StopStatus.Delivered)))
            .ToListAsync(ct);
        return new PagedResult<TripRow>(rows, total, page, size);
    }

    private async Task<DistributionTrip> LoadTripAsync(int id, CancellationToken ct) =>
        await db.DistributionTrips.Include(t => t.Stops).FirstOrDefaultAsync(t => t.Id == id, ct)
        ?? throw new NotFoundException("Trip not found.");

    private async Task<TripDetail> DetailAsync(DistributionTrip t, CancellationToken ct)
    {
        var ids = t.Stops.Select(s => s.DeliveryNoteId).ToList();
        var deliveries = await db.DeliveryNotes.AsNoTracking().Where(d => ids.Contains(d.Id)).ToDictionaryAsync(d => d.Id, ct);
        var stops = t.Stops.OrderBy(s => s.Sequence).Select(s =>
        {
            var d = deliveries[s.DeliveryNoteId];
            return new StopView(s.Id, s.Sequence, s.Status, s.Notes, s.DeliveryNoteId, d.Number, d.CustomerId, d.WarehouseId, d.CurrencyId, d.TotalAmount, d.Status);
        }).ToList();
        // Load check: weight of the goods on live (not cancelled) deliveries against the vehicle's capacity. Only a warning.
        var load = await (from l in db.DeliveryNoteLines
                          join d in db.DeliveryNotes on l.DeliveryNoteId equals d.Id
                          join it in db.Items on l.ItemId equals it.Id
                          where ids.Contains(d.Id) && d.Status != DocumentStatus.Cancelled
                          select new { l.Quantity, it.WeightKg }).AsNoTracking().ToListAsync(ct);
        var weight = load.Sum(x => x.Quantity * x.WeightKg);
        var capacity = await db.Vehicles.AsNoTracking().Where(v => v.Id == t.VehicleId).Select(v => v.CapacityKg).FirstOrDefaultAsync(ct);
        return new TripDetail(t.Id, t.Number, t.Date, t.DriverId, t.VehicleId, t.Status, t.Notes, t.StartedAt, t.CompletedAt, stops,
            weight, capacity, capacity > 0 && weight > capacity, load.Count(x => x.WeightKg == 0));
    }

    public async Task<TripDetail> GetTripAsync(int id, CancellationToken ct) => await DetailAsync(await LoadTripAsync(id, ct), ct);

    private static bool IsActive(TripStatus s) => s is TripStatus.Planned or TripStatus.InTransit;

    private async Task ValidateTripAsync(TripInput i, int? tripId, CancellationToken ct)
    {
        if (!await db.Drivers.AnyAsync(d => d.Id == i.DriverId && d.IsActive, ct)) throw new BusinessRuleException("The driver does not exist or is inactive.");
        if (!await db.Vehicles.AnyAsync(v => v.Id == i.VehicleId && v.IsActive, ct)) throw new BusinessRuleException("The vehicle does not exist or is inactive.");

        // One driver / one vehicle can only run one trip per day.
        var clash = await db.DistributionTrips.AsNoTracking()
            .Where(t => t.Id != tripId && t.Date == i.Date && (t.Status == TripStatus.Planned || t.Status == TripStatus.InTransit)
                        && (t.DriverId == i.DriverId || t.VehicleId == i.VehicleId))
            .Select(t => new { t.Number, SameDriver = t.DriverId == i.DriverId }).FirstOrDefaultAsync(ct);
        if (clash is not null)
            throw new BusinessRuleException(clash.SameDriver
                ? $"The driver already has trip {clash.Number} on that date."
                : $"The vehicle already has trip {clash.Number} on that date.");

        var ids = i.DeliveryNoteIds;
        var found = await db.DeliveryNotes.AsNoTracking().Where(d => ids.Contains(d.Id)).Select(d => new { d.Id, d.Number, d.Status }).ToListAsync(ct);
        if (found.Count != ids.Count) throw new BusinessRuleException("A delivery note does not exist.");
        var cancelled = found.FirstOrDefault(d => d.Status == DocumentStatus.Cancelled);
        if (cancelled is not null) throw new BusinessRuleException($"Delivery note {cancelled.Number} is cancelled and cannot be on a trip.");

        var handedOver = await (from s in db.TripStops
                                join t in db.DistributionTrips on s.TripId equals t.Id
                                where ids.Contains(s.DeliveryNoteId) && t.Status == TripStatus.Completed && s.Status == StopStatus.Delivered
                                select new { s.DeliveryNoteId, t.Number }).FirstOrDefaultAsync(ct);
        if (handedOver is not null)
            throw new BusinessRuleException($"Delivery note {found.First(d => d.Id == handedOver.DeliveryNoteId).Number} was already delivered on trip {handedOver.Number}.");

        var taken = await (from s in db.TripStops
                           join t in db.DistributionTrips on s.TripId equals t.Id
                           where ids.Contains(s.DeliveryNoteId) && t.Id != tripId && (t.Status == TripStatus.Planned || t.Status == TripStatus.InTransit)
                           select new { s.DeliveryNoteId, t.Number }).FirstOrDefaultAsync(ct);
        if (taken is not null)
            throw new BusinessRuleException($"Delivery note {found.First(d => d.Id == taken.DeliveryNoteId).Number} is already on trip {taken.Number}.");
    }

    private static List<TripStop> Stops(IReadOnlyList<int> deliveryIds) =>
        deliveryIds.Select((id, idx) => new TripStop { DeliveryNoteId = id, Sequence = idx + 1, Status = StopStatus.Pending }).ToList();

    public async Task<TripDetail> CreateTripAsync(TripInput i, CancellationToken ct)
    {
        await ValidateTripAsync(i, null, ct);
        var t = new DistributionTrip
        {
            Date = i.Date, DriverId = i.DriverId, VehicleId = i.VehicleId, Notes = Clean(i.Notes), Status = TripStatus.Planned,
            Stops = Stops(i.DeliveryNoteIds),
        };
        db.DistributionTrips.Add(t);
        await SaveAsync(ct);
        t.Number = $"TRP-{t.Id:D6}";
        await SaveAsync(ct);
        return await DetailAsync(t, ct);
    }

    public async Task<TripDetail> UpdateTripAsync(int id, TripInput i, CancellationToken ct)
    {
        var t = await LoadTripAsync(id, ct);
        if (t.Status != TripStatus.Planned) throw new BusinessRuleException("Only a planned trip can be edited.");
        await ValidateTripAsync(i, id, ct);

        t.Date = i.Date; t.DriverId = i.DriverId; t.VehicleId = i.VehicleId; t.Notes = Clean(i.Notes);
        db.TripStops.RemoveRange(t.Stops);
        t.Stops = Stops(i.DeliveryNoteIds);
        await SaveAsync(ct);
        return await DetailAsync(t, ct);
    }

    public async Task<TripDetail> StartTripAsync(int id, CancellationToken ct)
    {
        var t = await LoadTripAsync(id, ct);
        if (t.Status != TripStatus.Planned) throw new BusinessRuleException("Only a planned trip can be started.");
        if (t.Stops.Count == 0) throw new BusinessRuleException("A trip needs at least one stop before it can start.");

        // Goods only leave once the delivery notes are confirmed (that is what takes the stock out).
        var ids = t.Stops.Select(s => s.DeliveryNoteId).ToList();
        var notConfirmed = await db.DeliveryNotes.AsNoTracking().Where(d => ids.Contains(d.Id) && d.Status != DocumentStatus.Confirmed)
            .Select(d => d.Number).ToListAsync(ct);
        if (notConfirmed.Count > 0)
        {
            var list = string.Join(", ", notConfirmed);
            throw new BusinessRuleException($"These delivery notes must be confirmed before the trip starts: {list}.");
        }

        t.Status = TripStatus.InTransit;
        t.StartedAt = DateTime.UtcNow;
        await SaveAsync(ct);
        return await DetailAsync(t, ct);
    }

    public async Task<TripDetail> CompleteTripAsync(int id, CancellationToken ct)
    {
        var t = await LoadTripAsync(id, ct);
        if (t.Status != TripStatus.InTransit) throw new BusinessRuleException("Only a trip in transit can be completed.");
        if (t.Stops.Any(s => s.Status == StopStatus.Pending))
            throw new BusinessRuleException("Mark every stop as delivered or failed before completing the trip.");
        // A failed stop means the goods never left the truck: take the delivery back into stock, atomically with completing the trip.
        foreach (var stop in t.Stops.Where(s => s.Status == StopStatus.Failed))
        {
            var d = await LoadDeliveryAsync(stop.DeliveryNoteId, ct);
            if (d.Status != DocumentStatus.Cancelled) await CancelDeliveryCoreAsync(d, false, ct);
        }
        t.Status = TripStatus.Completed;
        t.CompletedAt = DateTime.UtcNow;
        await SaveAsync(ct);
        return await DetailAsync(t, ct);
    }

    public async Task<TripDetail> CancelTripAsync(int id, CancellationToken ct)
    {
        var t = await LoadTripAsync(id, ct);
        if (!IsActive(t.Status)) throw new BusinessRuleException("Only a planned or in-transit trip can be cancelled.");
        t.Status = TripStatus.Cancelled;
        await SaveAsync(ct);
        return await DetailAsync(t, ct);
    }

    public async Task<TripDetail> SetStopStatusAsync(int tripId, int stopId, StopStatusInput input, CancellationToken ct)
    {
        var t = await LoadTripAsync(tripId, ct);
        if (t.Status != TripStatus.InTransit) throw new BusinessRuleException("Stop results can only be recorded while the trip is in transit.");
        var stop = t.Stops.FirstOrDefault(s => s.Id == stopId) ?? throw new NotFoundException("Stop not found.");
        stop.Status = input.Status;
        stop.Notes = Clean(input.Notes);
        await SaveAsync(ct);
        return await DetailAsync(t, ct);
    }

    // =====================================================================
    //  Reports
    // =====================================================================

    /// <summary>Local amount a confirmed receipt cleared (allocated part at the invoice rate, the rest at its own rate).</summary>
    private static decimal Cleared(CustomerReceipt r) => r.LocalSettled ?? R4(r.Amount * r.ExchangeRate);

    public async Task<CustomerStatement> GetStatementAsync(int customerId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == customerId, ct)
                       ?? throw new NotFoundException("Customer not found.");

        var invoices = await db.SalesInvoices.AsNoTracking().Where(i => i.CustomerId == customerId && i.Status == DocumentStatus.Confirmed)
            .Select(i => new { i.Id, i.Date, i.Number, i.CurrencyId, i.TotalAmount, i.ExchangeRate }).ToListAsync(ct);
        var returns = await db.SalesReturns.AsNoTracking().Where(r => r.CustomerId == customerId && r.Status == DocumentStatus.Confirmed)
            .Select(r => new { r.Id, r.Date, r.Number, r.CurrencyId, r.TotalAmount, r.ExchangeRate }).ToListAsync(ct);
        var receipts = await db.CustomerReceipts.AsNoTracking().Where(r => r.CustomerId == customerId && r.Status == DocumentStatus.Confirmed).ToListAsync(ct);

        // Debit = they owe more (invoice); credit = they owe less (return, receipt). Same-day order: invoices, returns, receipts.
        var entries = invoices.Select(i => (i.Date, Order: 0, i.Id, Type: "Invoice", i.Number, Ref: (string?)null, i.CurrencyId, Amount: i.TotalAmount, i.ExchangeRate, Debit: R4(i.TotalAmount * i.ExchangeRate), Credit: 0m))
            .Concat(returns.Select(r => (r.Date, Order: 1, r.Id, Type: "Return", r.Number, Ref: (string?)null, r.CurrencyId, Amount: r.TotalAmount, r.ExchangeRate, Debit: 0m, Credit: R4(r.TotalAmount * r.ExchangeRate))))
            .Concat(receipts.Select(r => (r.Date, Order: 2, r.Id, Type: "Receipt", r.Number, Ref: r.Reference, r.CurrencyId, Amount: r.Amount, r.ExchangeRate, Debit: 0m, Credit: Cleared(r))))
            .OrderBy(e => e.Date).ThenBy(e => e.Order).ThenBy(e => e.Id).ToList();

        var opening = from is { } f ? entries.Where(e => e.Date < f).Sum(e => e.Debit - e.Credit) : 0m;
        var running = opening;
        var rows = new List<CustomerStatementRow>();
        foreach (var e in entries.Where(e => (from is null || e.Date >= from) && (to is null || e.Date <= to)))
        {
            running += e.Debit - e.Credit;
            rows.Add(new CustomerStatementRow(e.Date, e.Type, e.Number, e.Ref, e.CurrencyId, e.Amount, e.ExchangeRate, e.Debit, e.Credit, running));
        }
        return new CustomerStatement(customerId, from, to, opening, rows, rows.Sum(r => r.Debit), rows.Sum(r => r.Credit), running, customer.CreditLimit);
    }

    public async Task<IReadOnlyList<CustomerBalanceRow>> GetCustomerBalancesAsync(CancellationToken ct)
    {
        var invoiced = await db.SalesInvoices.AsNoTracking().Where(i => i.Status == DocumentStatus.Confirmed)
            .GroupBy(i => i.CustomerId).Select(g => new { Id = g.Key, Sum = g.Sum(i => Math.Round(i.TotalAmount * i.ExchangeRate, 4)) }).ToListAsync(ct);
        var returned = await db.SalesReturns.AsNoTracking().Where(r => r.Status == DocumentStatus.Confirmed)
            .GroupBy(r => r.CustomerId).Select(g => new { Id = g.Key, Sum = g.Sum(r => Math.Round(r.TotalAmount * r.ExchangeRate, 4)) }).ToListAsync(ct);
        var received = await db.CustomerReceipts.AsNoTracking().Where(r => r.Status == DocumentStatus.Confirmed)
            .GroupBy(r => r.CustomerId).Select(g => new { Id = g.Key, Sum = g.Sum(r => r.LocalSettled ?? Math.Round(r.Amount * r.ExchangeRate, 4)) }).ToListAsync(ct);

        var ids = invoiced.Select(x => x.Id).Union(returned.Select(x => x.Id)).Union(received.Select(x => x.Id)).ToList();
        var customers = await db.Customers.AsNoTracking().Where(c => ids.Contains(c.Id)).ToListAsync(ct);

        return customers.Select(c =>
        {
            var inv = invoiced.FirstOrDefault(x => x.Id == c.Id)?.Sum ?? 0m;
            var ret = returned.FirstOrDefault(x => x.Id == c.Id)?.Sum ?? 0m;
            var rec = received.FirstOrDefault(x => x.Id == c.Id)?.Sum ?? 0m;
            return new CustomerBalanceRow(c.Id, c.Code, c.NameAr, c.NameEn, inv, ret, rec, inv - ret - rec, c.CreditLimit);
        }).OrderByDescending(r => r.Balance).ThenBy(r => r.Code).ToList();
    }

    public async Task<IReadOnlyList<OpenInvoiceRow>> GetOpenInvoicesAsync(int? customerId, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var invoices = await db.SalesInvoices.AsNoTracking().Where(i => i.Status == DocumentStatus.Confirmed && (customerId == null || i.CustomerId == customerId))
            .Select(i => new { i.Id, i.Number, i.Date, i.DueDate, i.CurrencyId, i.ExchangeRate, i.TotalAmount }).ToListAsync(ct);
        var settled = (await db.CustomerReceiptAllocations.AsNoTracking()
            .Where(a => db.CustomerReceipts.Any(r => r.Id == a.CustomerReceiptId && r.Status == DocumentStatus.Confirmed)).ToListAsync(ct))
            .GroupBy(a => a.SalesInvoiceId).ToDictionary(g => g.Key, g => g.Sum(a => a.Amount));
        foreach (var g in (await db.SalesReturns.AsNoTracking().Where(r => r.Status == DocumentStatus.Confirmed && r.SalesInvoiceId != null).ToListAsync(ct)).GroupBy(r => r.SalesInvoiceId!.Value))
            settled[g.Key] = settled.GetValueOrDefault(g.Key) + g.Sum(r => r.TotalAmount);

        return invoices.Select(i =>
            {
                var done = settled.GetValueOrDefault(i.Id);
                var outstanding = i.TotalAmount - done;
                var (days, bucket) = Ageing.Of(i.DueDate ?? i.Date, today);
                return new OpenInvoiceRow(InvoiceKinds.SalesInvoice, i.Id, i.Number, i.Date, i.DueDate, i.CurrencyId, i.ExchangeRate, i.TotalAmount, done,
                    outstanding, R4(outstanding * i.ExchangeRate), days, bucket);
            })
            .Where(r => r.Outstanding > 0).OrderBy(r => r.DueDate ?? r.Date).ThenBy(r => r.InvoiceId).ToList();
    }

    public async Task<IReadOnlyList<AgeingRow>> GetAgeingAsync(CancellationToken ct)
    {
        var open = await GetOpenInvoicesAsync(null, ct);
        var customerOf = await db.SalesInvoices.AsNoTracking().Select(i => new { i.Id, i.CustomerId }).ToDictionaryAsync(i => i.Id, i => i.CustomerId, ct);
        var ids = open.Select(r => customerOf[r.InvoiceId]).Distinct().ToList();
        var customers = await db.Customers.AsNoTracking().Where(c => ids.Contains(c.Id)).ToListAsync(ct);
        return customers.Select(c => Ageing.Row(c.Id, c.Code, c.NameAr, c.NameEn, open.Where(r => customerOf[r.InvoiceId] == c.Id)))
            .OrderByDescending(r => r.Total).ThenBy(r => r.Code).ToList();
    }
}
