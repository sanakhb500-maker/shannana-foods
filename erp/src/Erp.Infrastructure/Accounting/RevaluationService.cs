using Erp.Application.Accounting;
using Erp.Application.Common;
using Erp.Domain.Entities;
using Erp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

using static Erp.Infrastructure.Common.DocumentRules;

namespace Erp.Infrastructure.Accounting;

public class RevaluationService(AppDbContext db, JournalPoster gl) : IRevaluationService
{
    private sealed record OpenItem(string Kind, int Id, string? Number, bool IsReceivable, int CurrencyId, decimal Rate, decimal Total, decimal Settled);

    /// <summary>Outstanding amount of every confirmed foreign-currency invoice as it stood on the date.</summary>
    private async Task<List<OpenItem>> OpenItemsAsync(DateOnly asOf, CancellationToken ct)
    {
        var localId = await db.Currencies.AsNoTracking().Where(c => c.IsLocal).Select(c => c.Id).FirstAsync(ct);
        var items = new List<OpenItem>();

        var sales = await db.SalesInvoices.AsNoTracking().Where(i => i.Status == DocumentStatus.Confirmed && i.Date <= asOf && i.CurrencyId != localId)
            .Select(i => new { i.Id, i.Number, i.CurrencyId, i.ExchangeRate, i.TotalAmount }).ToListAsync(ct);
        var receiptAllocs = (await db.CustomerReceiptAllocations.AsNoTracking()
            .Where(a => db.CustomerReceipts.Any(r => r.Id == a.CustomerReceiptId && r.Status == DocumentStatus.Confirmed && r.Date <= asOf)).ToListAsync(ct))
            .GroupBy(a => a.SalesInvoiceId).ToDictionary(g => g.Key, g => g.Sum(a => a.Amount));
        items.AddRange(sales.Select(i => new OpenItem(InvoiceKind.Sales, i.Id, i.Number, true, i.CurrencyId, i.ExchangeRate, i.TotalAmount, receiptAllocs.GetValueOrDefault(i.Id))));

        var payAllocs = await db.SupplierPaymentAllocations.AsNoTracking()
            .Where(a => db.SupplierPayments.Any(p => p.Id == a.SupplierPaymentId && p.Status == DocumentStatus.Confirmed && p.Date <= asOf)).ToListAsync(ct);
        var purchase = await db.PurchaseInvoices.AsNoTracking().Where(i => i.Status == DocumentStatus.Confirmed && i.Date <= asOf && i.CurrencyId != localId)
            .Select(i => new { i.Id, i.Number, i.CurrencyId, i.ExchangeRate, i.TotalAmount }).ToListAsync(ct);
        var byPurchase = payAllocs.Where(a => a.PurchaseInvoiceId != null).GroupBy(a => a.PurchaseInvoiceId!.Value).ToDictionary(g => g.Key, g => g.Sum(a => a.Amount));
        items.AddRange(purchase.Select(i => new OpenItem(InvoiceKind.Purchase, i.Id, i.Number, false, i.CurrencyId, i.ExchangeRate, i.TotalAmount, byPurchase.GetValueOrDefault(i.Id))));

        var expense = await db.ExpenseInvoices.AsNoTracking().Where(i => i.Status == DocumentStatus.Confirmed && i.Date <= asOf && i.CurrencyId != localId)
            .Select(i => new { i.Id, i.Number, i.CurrencyId, i.ExchangeRate, i.TotalAmount }).ToListAsync(ct);
        var byExpense = payAllocs.Where(a => a.ExpenseInvoiceId != null).GroupBy(a => a.ExpenseInvoiceId!.Value).ToDictionary(g => g.Key, g => g.Sum(a => a.Amount));
        items.AddRange(expense.Select(i => new OpenItem(InvoiceKind.Expense, i.Id, i.Number, false, i.CurrencyId, i.ExchangeRate, i.TotalAmount, byExpense.GetValueOrDefault(i.Id))));

        return items.Where(i => i.Total - i.Settled > 0).ToList();
    }

    private static class InvoiceKind
    {
        public const string Sales = "SalesInvoice";
        public const string Purchase = "PurchaseInvoice";
        public const string Expense = "ExpenseInvoice";
    }

    public async Task<RevaluationPreview> PreviewAsync(DateOnly asOf, CancellationToken ct)
    {
        var open = await OpenItemsAsync(asOf, ct);
        var closing = new Dictionary<int, decimal>();
        foreach (var currencyId in open.Select(o => o.CurrencyId).Distinct())
        {
            var rate = await db.ExchangeRates.AsNoTracking().Where(r => r.CurrencyId == currencyId && r.IsActive && r.Date <= asOf)
                .OrderByDescending(r => r.Date).Select(r => (decimal?)r.Rate).FirstOrDefaultAsync(ct);
            if (rate is null)
            {
                var code = await db.Currencies.AsNoTracking().Where(c => c.Id == currencyId).Select(c => c.Code).FirstAsync(ct);
                throw new BusinessRuleException($"No exchange rate for {code} on or before {asOf:yyyy-MM-dd}. Enter one.");
            }
            closing[currencyId] = rate.Value;
        }

        var lines = open.Select(o =>
        {
            var outstanding = o.Total - o.Settled;
            return new RevaluationLine(o.Kind, o.Id, o.Number, o.IsReceivable, o.CurrencyId, outstanding, o.Rate, closing[o.CurrencyId], R4(outstanding * (closing[o.CurrencyId] - o.Rate)));
        }).Where(l => l.Difference != 0).OrderBy(l => l.IsReceivable ? 0 : 1).ThenBy(l => l.Number).ToList();

        var receivables = lines.Where(l => l.IsReceivable).Sum(l => l.Difference);
        var payables = lines.Where(l => !l.IsReceivable).Sum(l => l.Difference);
        return new RevaluationPreview(asOf, lines, receivables, payables, receivables - payables); // a payable that grows is a loss
    }

    public async Task<RevaluationRunRow> RunAsync(DateOnly asOf, CancellationToken ct)
    {
        if (await db.RevaluationRuns.AnyAsync(r => r.AsOf == asOf, ct)) throw new BusinessRuleException("A revaluation for that date already exists.");
        var preview = await PreviewAsync(asOf, ct);
        if (preview.Lines.Count == 0) throw new BusinessRuleException("Nothing to revalue: no open foreign-currency invoice differs from the closing rate.");

        var r = preview.ReceivablesDifference;
        var p = preview.PayablesDifference;
        // Receivable that grew = gain (Dr Receivables / Cr Gain); payable that grew = loss (Dr Loss / Cr Payables); the reverse when they shrank.
        var debits = new[]
        {
            new PostLine(AccountRoles.Receivables, Math.Max(r, 0), Math.Max(-r, 0)),
            new PostLine(AccountRoles.Payables, Math.Max(-p, 0), Math.Max(p, 0)),
            new PostLine(AccountRoles.ExchangeLoss, Math.Max(-r, 0) + Math.Max(p, 0), 0),
            new PostLine(AccountRoles.ExchangeGain, 0, Math.Max(r, 0) + Math.Max(-p, 0)),
        };
        var mirror = debits.Select(l => l with { Debit = l.Credit, Credit = l.Debit }).ToArray();

        var next = asOf.AddDays(1);
        var entry = await gl.PostAsync(SourceTypes.Revaluation, null, asOf, $"Unrealised exchange revaluation at {asOf:yyyy-MM-dd}", ct, debits);
        var reversal = await gl.PostAsync(SourceTypes.Revaluation, null, next, $"Reversal of revaluation at {asOf:yyyy-MM-dd}", ct, mirror);

        var run = new RevaluationRun { AsOf = asOf, NetResult = preview.NetResult, JournalEntry = entry, ReversalEntry = reversal };
        db.RevaluationRuns.Add(run);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { throw new BusinessRuleException("A revaluation for that date already exists."); }
        return new RevaluationRunRow(run.Id, run.AsOf, run.NetResult, entry?.Number, reversal?.Number, run.CreatedAt, run.CreatedBy);
    }

    public async Task<IReadOnlyList<RevaluationRunRow>> ListAsync(CancellationToken ct) =>
        await db.RevaluationRuns.AsNoTracking().OrderByDescending(r => r.AsOf)
            .Select(r => new RevaluationRunRow(r.Id, r.AsOf, r.NetResult, r.JournalEntry!.Number, r.ReversalEntry!.Number, r.CreatedAt, r.CreatedBy)).ToListAsync(ct);
}
