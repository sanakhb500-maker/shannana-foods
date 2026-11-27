using Erp.Application.Common;
using Erp.Application.Expenses;
using Erp.Domain.Entities;
using Erp.Infrastructure.Accounting;
using Erp.Infrastructure.Common;
using Erp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

using static Erp.Infrastructure.Common.DocumentRules;

namespace Erp.Infrastructure.Expenses;

public class ExpenseService(AppDbContext db, ICurrentUser user, JournalPoster gl, DocumentNumbering numbers) : IExpenseService
{
    private async Task SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new ConcurrencyConflictException(); }
    }

    private static decimal Total(IEnumerable<ExpenseLineInput> lines) => lines.Sum(l => R4(l.Amount));

    /// <summary>Lines must name active, postable EXPENSE accounts.</summary>
    private async Task EnsureExpenseAccountsAsync(IEnumerable<int> accountIds, CancellationToken ct)
    {
        var ids = accountIds.Distinct().ToList();
        var accounts = await db.Accounts.AsNoTracking().Where(a => ids.Contains(a.Id)).ToListAsync(ct);
        if (accounts.Count != ids.Count) throw new BusinessRuleException("An account does not exist.");
        if (accounts.Any(a => !a.IsActive || !a.IsPostable)) throw new BusinessRuleException("An expense line uses an inactive or header account.");
        if (accounts.Any(a => a.Type != AccountType.Expense)) throw new BusinessRuleException("Expense lines must use an expense account.");
    }

    /// <summary>
    /// Local amount of each line at the rate, adjusted by the last line so the lines add up to exactly R4(total x rate):
    /// that is what the supplier statement and the payables control account use, so the books and the sub-ledger agree to the cent.
    /// </summary>
    private static List<decimal> LocalAmounts(IReadOnlyCollection<decimal> amounts, decimal rate)
    {
        var local = amounts.Select(a => R4(a * rate)).ToList();
        var diff = R4(amounts.Sum(a => R4(a)) * rate) - local.Sum();
        if (diff != 0 && local.Count > 0) local[^1] += diff;
        return local;
    }

    // =====================================================================
    //  Expense invoices (supplier bills for services)
    // =====================================================================

    public async Task<PagedResult<ExpenseInvoiceRow>> ListInvoicesAsync(ExpenseFilter f, CancellationToken ct)
    {
        var q = db.ExpenseInvoices.AsNoTracking().AsQueryable();
        if (f.Status is { } st) q = q.Where(i => i.Status == st);
        if (f.SupplierId is { } sid) q = q.Where(i => i.SupplierId == sid);
        if (!string.IsNullOrWhiteSpace(f.Q))
        {
            var s = f.Q.Trim();
            q = q.Where(i => (i.Number != null && i.Number.Contains(s)) || (i.SupplierInvoiceNumber != null && i.SupplierInvoiceNumber.Contains(s)));
        }
        var (page, size) = Paging(f.Page, f.PageSize);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(i => i.Date).ThenByDescending(i => i.Id).Skip((page - 1) * size).Take(size)
            .Select(i => new ExpenseInvoiceRow(i.Id, i.Number, i.Date, i.DueDate, i.SupplierId, i.SupplierInvoiceNumber, i.CurrencyId, i.Status, i.TotalAmount)).ToListAsync(ct);
        return new PagedResult<ExpenseInvoiceRow>(rows, total, page, size);
    }

    public async Task<ExpenseInvoice> GetInvoiceAsync(int id, CancellationToken ct) =>
        await db.ExpenseInvoices.AsNoTracking().Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException("Expense invoice not found.");

    private async Task<ExpenseInvoice> LoadInvoiceAsync(int id, CancellationToken ct) =>
        await db.ExpenseInvoices.Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException("Expense invoice not found.");

    private static List<ExpenseInvoiceLine> InvoiceLines(IEnumerable<ExpenseLineInput> lines) =>
        lines.Select(l => new ExpenseInvoiceLine { AccountId = l.AccountId, Description = Clean(l.Description), Amount = R4(l.Amount) }).ToList();

    private async Task EnsureSupplierAsync(int supplierId, CancellationToken ct)
    {
        if (!await db.Suppliers.AnyAsync(s => s.Id == supplierId && s.IsActive, ct)) throw new BusinessRuleException("The supplier does not exist or is inactive.");
    }

    public async Task<ExpenseInvoice> CreateInvoiceAsync(ExpenseInvoiceInput i, CancellationToken ct)
    {
        await EnsureSupplierAsync(i.SupplierId, ct);
        await EnsureExpenseAccountsAsync(i.Lines.Select(l => l.AccountId), ct);
        var rate = await db.ResolveRateAsync(i.CurrencyId, i.Date, i.ExchangeRate, null, ct);
        var e = new ExpenseInvoice
        {
            Date = i.Date, DueDate = i.DueDate, SupplierId = i.SupplierId, SupplierInvoiceNumber = Clean(i.SupplierInvoiceNumber), CurrencyId = i.CurrencyId,
            ExchangeRate = rate, Notes = Clean(i.Notes), Status = DocumentStatus.Draft, Lines = InvoiceLines(i.Lines), TotalAmount = Total(i.Lines),
        };
        db.ExpenseInvoices.Add(e);
        await SaveAsync(ct);
        return e;
    }

    public async Task<ExpenseInvoice> UpdateInvoiceAsync(int id, ExpenseInvoiceInput i, CancellationToken ct)
    {
        var e = await LoadInvoiceAsync(id, ct);
        EnsureDraft(e.Status);
        await EnsureSupplierAsync(i.SupplierId, ct);
        await EnsureExpenseAccountsAsync(i.Lines.Select(l => l.AccountId), ct);
        var rate = await db.ResolveRateAsync(i.CurrencyId, i.Date, i.ExchangeRate, e.CurrencyId == i.CurrencyId ? e.ExchangeRate : null, ct);
        e.Date = i.Date; e.DueDate = i.DueDate; e.SupplierId = i.SupplierId; e.SupplierInvoiceNumber = Clean(i.SupplierInvoiceNumber);
        e.CurrencyId = i.CurrencyId; e.ExchangeRate = rate; e.Notes = Clean(i.Notes);
        db.ExpenseInvoiceLines.RemoveRange(e.Lines);
        e.Lines = InvoiceLines(i.Lines);
        e.TotalAmount = Total(i.Lines);
        await SaveAsync(ct);
        return e;
    }

    public async Task<ExpenseInvoice> ConfirmInvoiceAsync(int id, CancellationToken ct)
    {
        var e = await LoadInvoiceAsync(id, ct);
        EnsureDraft(e.Status);
        e.Number = await numbers.NextAsync(SequenceKeys.ExpenseInvoice, ct);
        await EnsureSupplierAsync(e.SupplierId, ct);
        await EnsureExpenseAccountsAsync(e.Lines.Select(l => l.AccountId), ct);

        // Dr each expense account / Cr Payables (supplier)
        var local = LocalAmounts(e.Lines.Select(l => l.Amount).ToList(), e.ExchangeRate);
        var lines = e.Lines.Select((l, idx) => new PostLine("", local[idx], 0, AccountId: l.AccountId)).ToList();
        lines.Add(new PostLine(AccountRoles.Payables, 0, local.Sum(), PartyType.Supplier, e.SupplierId));
        await gl.PostAsync(SourceTypes.ExpenseInvoice, e.Id, e.Date, $"Expense invoice {e.Number}", ct, [.. lines]);

        e.Status = DocumentStatus.Confirmed;
        e.ConfirmedAt = DateTime.UtcNow;
        e.ConfirmedBy = user.UserName;
        await SaveAsync(ct);
        return e;
    }

    public async Task<ExpenseInvoice> CancelInvoiceAsync(int id, CancellationToken ct)
    {
        var e = await LoadInvoiceAsync(id, ct);
        if (e.Status == DocumentStatus.Cancelled) throw new BusinessRuleException("The document is already cancelled.");
        if (e.Status == DocumentStatus.Confirmed)
        {
            if (await db.SupplierPaymentAllocations.AnyAsync(a => a.ExpenseInvoiceId == id &&
                    db.SupplierPayments.Any(p => p.Id == a.SupplierPaymentId && p.Status == DocumentStatus.Confirmed), ct))
                throw new BusinessRuleException("Payments are already allocated to this invoice. Cancel them first.");
            await gl.ReverseAsync(SourceTypes.ExpenseInvoice, e.Id, $"Cancelled expense invoice {e.Number}", ct);
        }
        e.Status = DocumentStatus.Cancelled;
        await SaveAsync(ct);
        return e;
    }

    // =====================================================================
    //  Expense vouchers (paid on the spot)
    // =====================================================================

    public async Task<PagedResult<ExpenseVoucherRow>> ListVouchersAsync(ExpenseFilter f, CancellationToken ct)
    {
        var q = db.ExpenseVouchers.AsNoTracking().AsQueryable();
        if (f.Status is { } st) q = q.Where(v => v.Status == st);
        if (!string.IsNullOrWhiteSpace(f.Q))
        {
            var s = f.Q.Trim();
            q = q.Where(v => (v.Number != null && v.Number.Contains(s)) || (v.Payee != null && v.Payee.Contains(s)));
        }
        var (page, size) = Paging(f.Page, f.PageSize);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(v => v.Date).ThenByDescending(v => v.Id).Skip((page - 1) * size).Take(size)
            .Select(v => new ExpenseVoucherRow(v.Id, v.Number, v.Date, v.Payee, v.Method, v.CurrencyId, v.Status, v.TotalAmount)).ToListAsync(ct);
        return new PagedResult<ExpenseVoucherRow>(rows, total, page, size);
    }

    public async Task<ExpenseVoucher> GetVoucherAsync(int id, CancellationToken ct) =>
        await db.ExpenseVouchers.AsNoTracking().Include(v => v.Lines).FirstOrDefaultAsync(v => v.Id == id, ct) ?? throw new NotFoundException("Expense voucher not found.");

    private async Task<ExpenseVoucher> LoadVoucherAsync(int id, CancellationToken ct) =>
        await db.ExpenseVouchers.Include(v => v.Lines).FirstOrDefaultAsync(v => v.Id == id, ct) ?? throw new NotFoundException("Expense voucher not found.");

    private static List<ExpenseVoucherLine> VoucherLines(IEnumerable<ExpenseLineInput> lines) =>
        lines.Select(l => new ExpenseVoucherLine { AccountId = l.AccountId, Description = Clean(l.Description), Amount = R4(l.Amount) }).ToList();

    public async Task<ExpenseVoucher> CreateVoucherAsync(ExpenseVoucherInput i, CancellationToken ct)
    {
        await EnsureExpenseAccountsAsync(i.Lines.Select(l => l.AccountId), ct);
        var rate = await db.ResolveRateAsync(i.CurrencyId, i.Date, i.ExchangeRate, null, ct);
        var v = new ExpenseVoucher
        {
            Date = i.Date, CurrencyId = i.CurrencyId, ExchangeRate = rate, Method = i.Method, Payee = Clean(i.Payee), Notes = Clean(i.Notes),
            Status = DocumentStatus.Draft, Lines = VoucherLines(i.Lines), TotalAmount = Total(i.Lines),
        };
        db.ExpenseVouchers.Add(v);
        await SaveAsync(ct);
        return v;
    }

    public async Task<ExpenseVoucher> UpdateVoucherAsync(int id, ExpenseVoucherInput i, CancellationToken ct)
    {
        var v = await LoadVoucherAsync(id, ct);
        EnsureDraft(v.Status);
        await EnsureExpenseAccountsAsync(i.Lines.Select(l => l.AccountId), ct);
        var rate = await db.ResolveRateAsync(i.CurrencyId, i.Date, i.ExchangeRate, v.CurrencyId == i.CurrencyId ? v.ExchangeRate : null, ct);
        v.Date = i.Date; v.CurrencyId = i.CurrencyId; v.ExchangeRate = rate; v.Method = i.Method; v.Payee = Clean(i.Payee); v.Notes = Clean(i.Notes);
        db.ExpenseVoucherLines.RemoveRange(v.Lines);
        v.Lines = VoucherLines(i.Lines);
        v.TotalAmount = Total(i.Lines);
        await SaveAsync(ct);
        return v;
    }

    public async Task<ExpenseVoucher> ConfirmVoucherAsync(int id, CancellationToken ct)
    {
        var v = await LoadVoucherAsync(id, ct);
        EnsureDraft(v.Status);
        v.Number = await numbers.NextAsync(SequenceKeys.ExpenseVoucher, ct);
        await EnsureExpenseAccountsAsync(v.Lines.Select(l => l.AccountId), ct);

        // Dr each expense account / Cr Cash (cash) or Bank (transfer, cheque)
        var local = LocalAmounts(v.Lines.Select(l => l.Amount).ToList(), v.ExchangeRate);
        var lines = v.Lines.Select((l, idx) => new PostLine("", local[idx], 0, AccountId: l.AccountId)).ToList();
        lines.Add(new PostLine(v.Method == PaymentMethod.Cash ? AccountRoles.Cash : AccountRoles.Bank, 0, local.Sum()));
        await gl.PostAsync(SourceTypes.ExpenseVoucher, v.Id, v.Date, $"Expense voucher {v.Number}", ct, [.. lines]);

        v.Status = DocumentStatus.Confirmed;
        v.ConfirmedAt = DateTime.UtcNow;
        v.ConfirmedBy = user.UserName;
        await SaveAsync(ct);
        return v;
    }

    public async Task<ExpenseVoucher> CancelVoucherAsync(int id, CancellationToken ct)
    {
        var v = await LoadVoucherAsync(id, ct);
        if (v.Status == DocumentStatus.Cancelled) throw new BusinessRuleException("The document is already cancelled.");
        if (v.Status == DocumentStatus.Confirmed) await gl.ReverseAsync(SourceTypes.ExpenseVoucher, v.Id, $"Cancelled expense voucher {v.Number}", ct);
        v.Status = DocumentStatus.Cancelled;
        await SaveAsync(ct);
        return v;
    }
}
