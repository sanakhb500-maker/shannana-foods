using Erp.Application.Accounting;
using Erp.Application.Common;
using Erp.Domain.Entities;
using Erp.Infrastructure.Common;
using Erp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

using static Erp.Infrastructure.Common.DocumentRules;

namespace Erp.Infrastructure.Accounting;

public class AccountingService(AppDbContext db, ICurrentUser user, JournalPoster gl, DocumentNumbering numbers) : IAccountingService
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private async Task SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new ConcurrencyConflictException(); }
    }

    /// <summary>Every line that counts in the books (anything not a draft), with its entry date.</summary>
    private IQueryable<BookLine> Books() =>
        from l in db.JournalLines.AsNoTracking()
        join e in db.JournalEntries.AsNoTracking() on l.JournalEntryId equals e.Id
        where e.Status != JournalStatus.Draft
        select new BookLine { AccountId = l.AccountId, Date = e.Date, Debit = l.Debit, Credit = l.Credit };

    // A class with member-init (not a positional record) so EF can translate grouping and sums over it to SQL.
    private sealed class BookLine
    {
        public int AccountId { get; init; }
        public DateOnly Date { get; init; }
        public decimal Debit { get; init; }
        public decimal Credit { get; init; }
    }

    // =====================================================================
    //  Manual journal entries
    // =====================================================================

    public async Task<PagedResult<JournalRow>> ListEntriesAsync(JournalFilter f, CancellationToken ct)
    {
        var q = db.JournalEntries.AsNoTracking().AsQueryable();
        if (f.Status is { } st) q = q.Where(e => e.Status == st);
        if (!string.IsNullOrWhiteSpace(f.SourceType)) { var t = f.SourceType.Trim(); q = q.Where(e => e.SourceType == t); }
        if (f.From is { } from) q = q.Where(e => e.Date >= from);
        if (f.To is { } to) q = q.Where(e => e.Date <= to);
        if (!string.IsNullOrWhiteSpace(f.Q))
        {
            var s = f.Q.Trim();
            q = q.Where(e => e.Description.Contains(s));
        }

        var (page, size) = Paging(f.Page, f.PageSize);
        var total = await q.CountAsync(ct);
        var raw = await q.OrderByDescending(e => e.Date).ThenByDescending(e => e.Id).Skip((page - 1) * size).Take(size)
            .Select(e => new { e.Id, e.Number, e.Date, e.Description, e.SourceType, e.SourceId, e.Status, Total = e.Lines.Sum(l => l.Debit) }).ToListAsync(ct);
        var rows = raw.Select(e => new JournalRow(e.Id, e.Number, e.Date, e.Description, e.SourceType, e.SourceId, e.Status, e.Total)).ToList();
        return new PagedResult<JournalRow>(rows, total, page, size);
    }

    public async Task<JournalEntry> GetEntryAsync(int id, CancellationToken ct) =>
        await db.JournalEntries.AsNoTracking().Include(e => e.Lines).FirstOrDefaultAsync(e => e.Id == id, ct)
        ?? throw new NotFoundException("Journal entry not found.");

    private async Task<JournalEntry> LoadEntryAsync(int id, CancellationToken ct) =>
        await db.JournalEntries.Include(e => e.Lines).FirstOrDefaultAsync(e => e.Id == id, ct)
        ?? throw new NotFoundException("Journal entry not found.");

    private async Task ValidateLinesAsync(JournalEntryInput i, CancellationToken ct)
    {
        var debit = i.Lines.Sum(l => l.Debit);
        var credit = i.Lines.Sum(l => l.Credit);
        if (debit != credit) throw new BusinessRuleException($"The entry is not balanced: debits {debit:0.####} vs credits {credit:0.####}.");

        var ids = i.Lines.Select(l => l.AccountId).Distinct().ToList();
        var accounts = await db.Accounts.AsNoTracking().Where(a => ids.Contains(a.Id)).ToListAsync(ct);
        if (accounts.Count != ids.Count) throw new BusinessRuleException("An account does not exist.");
        var bad = accounts.FirstOrDefault(a => !a.IsActive || !a.IsPostable);
        if (bad is not null) throw new BusinessRuleException($"Account {bad.Code} is inactive or is a header account that cannot take postings.");

        foreach (var l in i.Lines.Where(l => l.PartyType != PartyType.None))
        {
            var exists = l.PartyType == PartyType.Supplier
                ? await db.Suppliers.AnyAsync(s => s.Id == l.PartyId, ct)
                : await db.Customers.AnyAsync(c => c.Id == l.PartyId, ct);
            if (!exists) throw new BusinessRuleException("A line's supplier/customer does not exist.");
        }
    }

    private static List<JournalLine> ToLines(IEnumerable<JournalLineInput> lines) =>
        lines.Select(l => new JournalLine
        {
            AccountId = l.AccountId, Debit = R4(l.Debit), Credit = R4(l.Credit), Description = Clean(l.Description),
            PartyType = l.PartyType, PartyId = l.PartyType == PartyType.None ? null : l.PartyId,
        }).ToList();

    public async Task<JournalEntry> CreateEntryAsync(JournalEntryInput i, CancellationToken ct)
    {
        await ValidateLinesAsync(i, ct);
        var e = new JournalEntry { Date = i.Date, Description = i.Description.Trim(), SourceType = SourceTypes.Manual, Status = JournalStatus.Draft, Lines = ToLines(i.Lines) };
        db.JournalEntries.Add(e);
        await SaveAsync(ct);
        return e;
    }

    public async Task<JournalEntry> UpdateEntryAsync(int id, JournalEntryInput i, CancellationToken ct)
    {
        var e = await LoadEntryAsync(id, ct);
        EnsureManualDraft(e, "edited");
        await ValidateLinesAsync(i, ct);
        e.Date = i.Date;
        e.Description = i.Description.Trim();
        db.JournalLines.RemoveRange(e.Lines);
        e.Lines = ToLines(i.Lines);
        await SaveAsync(ct);
        return e;
    }

    public async Task<JournalEntry> PostEntryAsync(int id, CancellationToken ct)
    {
        var e = await LoadEntryAsync(id, ct);
        EnsureManualDraft(e, "posted");
        await gl.EnsureOpenAsync(e.Date, ct);
        // accounts may have been switched off since the draft was saved
        var ids = e.Lines.Select(l => l.AccountId).Distinct().ToList();
        if (await db.Accounts.CountAsync(a => ids.Contains(a.Id) && a.IsActive && a.IsPostable, ct) != ids.Count)
            throw new BusinessRuleException("An account on this entry is no longer active or postable.");
        e.Number = await numbers.NextAsync(SequenceKeys.Journal, ct);
        e.Status = JournalStatus.Posted;
        e.PostedAt = DateTime.UtcNow;
        e.PostedBy = user.UserName;
        await SaveAsync(ct);
        return e;
    }

    public async Task DeleteDraftAsync(int id, CancellationToken ct)
    {
        var e = await LoadEntryAsync(id, ct);
        EnsureManualDraft(e, "deleted");
        db.JournalEntries.Remove(e);
        await SaveAsync(ct);
    }

    public async Task<JournalEntry> ReverseEntryAsync(int id, CancellationToken ct)
    {
        var e = await LoadEntryAsync(id, ct);
        if (e.SourceType != SourceTypes.Manual)
            throw new BusinessRuleException("Entries generated by a document are reversed by cancelling that document.");
        if (e.Status != JournalStatus.Posted || e.ReversalOfId is not null)
            throw new BusinessRuleException("Only a posted, original entry can be reversed.");

        await gl.EnsureOpenAsync(Today, ct);
        var rev = JournalPoster.ReversalOf(e, Today, $"Reversal of {e.Number}: {e.Description}", user.UserName, await numbers.NextAsync(SequenceKeys.Journal, ct));
        db.JournalEntries.Add(rev);
        e.Status = JournalStatus.Reversed;
        await SaveAsync(ct);
        return rev;
    }

    private static void EnsureManualDraft(JournalEntry e, string verb)
    {
        if (e.SourceType != SourceTypes.Manual) throw new BusinessRuleException("Entries generated by a document cannot be changed here.");
        if (e.Status == JournalStatus.Draft) return;
        throw new BusinessRuleException(verb switch
        {
            "edited" => "Only a draft entry can be edited.",
            "posted" => "Only a draft entry can be posted.",
            _ => "Only a draft entry can be deleted.",
        });
    }

    // =====================================================================
    //  Reports
    // =====================================================================

    public async Task<TrialBalance> GetTrialBalanceAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var upTo = Books();
        if (to is { } t) upTo = upTo.Where(x => x.Date <= t);

        var opening = from is { } f
            ? await upTo.Where(x => x.Date < f).GroupBy(x => x.AccountId).Select(g => new { Id = g.Key, Net = g.Sum(x => x.Debit - x.Credit) }).ToListAsync(ct)
            : [];
        var inPeriod = from is { } f2 ? upTo.Where(x => x.Date >= f2) : upTo;
        var period = await inPeriod.GroupBy(x => x.AccountId)
            .Select(g => new { Id = g.Key, Debit = g.Sum(x => x.Debit), Credit = g.Sum(x => x.Credit) }).ToListAsync(ct);

        var ids = opening.Select(x => x.Id).Union(period.Select(x => x.Id)).ToList();
        var accounts = await db.Accounts.AsNoTracking().Where(a => ids.Contains(a.Id)).ToListAsync(ct);

        var rows = accounts.OrderBy(a => a.Code).Select(a =>
        {
            var open = opening.FirstOrDefault(x => x.Id == a.Id)?.Net ?? 0m;
            var p = period.FirstOrDefault(x => x.Id == a.Id);
            var d = p?.Debit ?? 0m;
            var c = p?.Credit ?? 0m;
            return new TrialBalanceRow(a.Id, a.Code, a.NameAr, a.NameEn, a.Type, open, d, c, open + d - c);
        }).ToList();

        var totalDebit = rows.Sum(r => r.Debit);
        var totalCredit = rows.Sum(r => r.Credit);
        var closingDebit = rows.Where(r => r.Closing > 0).Sum(r => r.Closing);
        var closingCredit = -rows.Where(r => r.Closing < 0).Sum(r => r.Closing);
        return new TrialBalance(from, to, rows, totalDebit, totalCredit, closingDebit, closingCredit,
            totalDebit == totalCredit && closingDebit == closingCredit);
    }

    public async Task<Ledger> GetLedgerAsync(int accountId, DateOnly? from, DateOnly? to, PartyType partyType, int? partyId, CancellationToken ct)
    {
        var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId, ct) ?? throw new NotFoundException("Account not found.");

        var q = from l in db.JournalLines.AsNoTracking()
                join e in db.JournalEntries.AsNoTracking() on l.JournalEntryId equals e.Id
                where e.Status != JournalStatus.Draft && l.AccountId == accountId
                select new { EntryId = e.Id, e.Number, e.Date, e.Description, e.SourceType, LineId = l.Id, l.Debit, l.Credit, l.PartyType, l.PartyId };
        if (partyType != PartyType.None) q = q.Where(x => x.PartyType == partyType && x.PartyId == partyId);
        if (to is { } t) q = q.Where(x => x.Date <= t);

        var opening = from is { } f ? await q.Where(x => x.Date < f).SumAsync(x => (decimal?)(x.Debit - x.Credit), ct) ?? 0m : 0m;
        var inRange = from is { } f2 ? q.Where(x => x.Date >= f2) : q;
        var raw = await inRange.OrderBy(x => x.Date).ThenBy(x => x.EntryId).ThenBy(x => x.LineId).ToListAsync(ct);

        var running = opening;
        var rows = new List<LedgerRow>();
        foreach (var x in raw)
        {
            running += x.Debit - x.Credit;
            rows.Add(new LedgerRow(x.Date, x.EntryId, x.Number, x.Description, x.SourceType, x.Debit, x.Credit, running, x.PartyType, x.PartyId));
        }
        return new Ledger(account.Id, account.Code, account.NameAr, account.NameEn, account.Type, from, to, opening, rows,
            rows.Sum(r => r.Debit), rows.Sum(r => r.Credit), running);
    }

    /// <summary>Net debit (debit - credit) per account over a date range, for the given account types.</summary>
    private async Task<List<(Account Account, decimal Net)>> NetByAccountAsync(DateOnly? from, DateOnly? to, AccountType[] types, CancellationToken ct)
    {
        var q = Books();
        if (from is { } f) q = q.Where(x => x.Date >= f);
        if (to is { } t) q = q.Where(x => x.Date <= t);
        var sums = await q.GroupBy(x => x.AccountId).Select(g => new { Id = g.Key, Net = g.Sum(x => x.Debit - x.Credit) }).ToListAsync(ct);

        var ids = sums.Select(s => s.Id).ToList();
        var accounts = await db.Accounts.AsNoTracking().Where(a => ids.Contains(a.Id) && types.Contains(a.Type)).OrderBy(a => a.Code).ToListAsync(ct);
        return accounts.Select(a => (a, sums.First(s => s.Id == a.Id).Net)).ToList();
    }

    private static StatementLine Line(Account a, decimal amount) => new(a.Id, a.Code, a.NameAr, a.NameEn, amount);

    public async Task<IncomeStatement> GetIncomeStatementAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var net = await NetByAccountAsync(from, to, [AccountType.Revenue, AccountType.Expense], ct);
        var revenue = net.Where(x => x.Account.Type == AccountType.Revenue && x.Net != 0).Select(x => Line(x.Account, -x.Net)).ToList(); // credit-normal
        var expenses = net.Where(x => x.Account.Type == AccountType.Expense && x.Net != 0).Select(x => Line(x.Account, x.Net)).ToList();
        var totalRevenue = revenue.Sum(l => l.Amount);
        var totalExpenses = expenses.Sum(l => l.Amount);
        return new IncomeStatement(from, to, revenue, totalRevenue, expenses, totalExpenses, totalRevenue - totalExpenses);
    }

    public async Task<BalanceSheet> GetBalanceSheetAsync(DateOnly? asOf, CancellationToken ct)
    {
        var net = await NetByAccountAsync(null, asOf, [AccountType.Asset, AccountType.Liability, AccountType.Equity, AccountType.Revenue, AccountType.Expense], ct);
        var assets = net.Where(x => x.Account.Type == AccountType.Asset && x.Net != 0).Select(x => Line(x.Account, x.Net)).ToList();
        var liabilities = net.Where(x => x.Account.Type == AccountType.Liability && x.Net != 0).Select(x => Line(x.Account, -x.Net)).ToList();
        var equity = net.Where(x => x.Account.Type == AccountType.Equity && x.Net != 0).Select(x => Line(x.Account, -x.Net)).ToList();
        // Profit so far has not been closed into equity yet, so it is shown as its own line.
        var earnings = -net.Where(x => x.Account.Type is AccountType.Revenue or AccountType.Expense).Sum(x => x.Net);

        var totalAssets = assets.Sum(l => l.Amount);
        var totalLiabilities = liabilities.Sum(l => l.Amount);
        var totalEquity = equity.Sum(l => l.Amount) + earnings;
        return new BalanceSheet(asOf, assets, totalAssets, liabilities, totalLiabilities, equity, earnings, totalEquity, totalAssets == totalLiabilities + totalEquity);
    }

    private async Task<decimal> RoleNetDebitAsync(string role, CancellationToken ct, bool excludeRevaluation = false)
    {
        var id = await db.Accounts.AsNoTracking().Where(a => a.SystemRole == role).Select(a => (int?)a.Id).FirstOrDefaultAsync(ct);
        if (id is null) return 0m;
        return await db.JournalLines.AsNoTracking()
            .Where(l => db.JournalEntries.Any(e => e.Id == l.JournalEntryId && e.Status != JournalStatus.Draft && (!excludeRevaluation || e.SourceType != SourceTypes.Revaluation)) && l.AccountId == id)
            .SumAsync(l => (decimal?)(l.Debit - l.Credit), ct) ?? 0m;
    }

    public async Task<IReadOnlyList<ReconciliationRow>> GetReconciliationAsync(CancellationToken ct)
    {
        // General ledger side
        // Unrealised exchange differences (revaluation entries) are timing items, not sub-ledger differences: leave them out.
        var glInventory = await RoleNetDebitAsync(AccountRoles.Inventory, ct, true);
        var glReceivables = await RoleNetDebitAsync(AccountRoles.Receivables, ct, true);
        var glPayables = -await RoleNetDebitAsync(AccountRoles.Payables, ct, true); // credit-normal: show as a positive amount owed

        // Sub-ledger side
        var stock = R4(await db.StockBalances.AsNoTracking().SumAsync(b => (decimal?)(b.Quantity * b.AverageCost), ct) ?? 0m);

        var supInvoiced = await db.PurchaseInvoices.AsNoTracking().Where(i => i.Status == DocumentStatus.Confirmed).SumAsync(i => (decimal?)Math.Round(i.TotalAmount * i.ExchangeRate, 4), ct) ?? 0m;
        var supExpenses = await db.ExpenseInvoices.AsNoTracking().Where(i => i.Status == DocumentStatus.Confirmed).SumAsync(i => (decimal?)Math.Round(i.TotalAmount * i.ExchangeRate, 4), ct) ?? 0m;
        var supReturns = await db.PurchaseReturns.AsNoTracking().Where(r => r.Status == DocumentStatus.Confirmed).SumAsync(r => (decimal?)Math.Round(r.TotalAmount * r.ExchangeRate, 4), ct) ?? 0m;
        var supPaid = await db.SupplierPayments.AsNoTracking().Where(p => p.Status == DocumentStatus.Confirmed).SumAsync(p => (decimal?)(p.LocalSettled ?? Math.Round(p.Amount * p.ExchangeRate, 4)), ct) ?? 0m;

        var cusInvoiced = await db.SalesInvoices.AsNoTracking().Where(i => i.Status == DocumentStatus.Confirmed).SumAsync(i => (decimal?)Math.Round(i.TotalAmount * i.ExchangeRate, 4), ct) ?? 0m;
        var cusReturned = await db.SalesReturns.AsNoTracking().Where(r => r.Status == DocumentStatus.Confirmed).SumAsync(r => (decimal?)Math.Round(r.TotalAmount * r.ExchangeRate, 4), ct) ?? 0m;
        var cusReceived = await db.CustomerReceipts.AsNoTracking().Where(r => r.Status == DocumentStatus.Confirmed).SumAsync(r => (decimal?)(r.LocalSettled ?? Math.Round(r.Amount * r.ExchangeRate, 4)), ct) ?? 0m;

        static ReconciliationRow Row(string key, decimal gl, decimal sub) => new(key, gl, sub, gl - sub, Math.Abs(gl - sub) <= 0.01m);
        return
        [
            Row("Inventory", glInventory, stock),
            Row("Payables", glPayables, supInvoiced + supExpenses - supReturns - supPaid),
            Row("Receivables", glReceivables, cusInvoiced - cusReturned - cusReceived),
        ];
    }

    public async Task<AccountingSummary> GetSummaryAsync(CancellationToken ct)
    {
        var cash = await RoleNetDebitAsync(AccountRoles.Cash, ct) + await RoleNetDebitAsync(AccountRoles.Bank, ct);
        var receivables = await RoleNetDebitAsync(AccountRoles.Receivables, ct);
        var payables = -await RoleNetDebitAsync(AccountRoles.Payables, ct);
        var inventory = await RoleNetDebitAsync(AccountRoles.Inventory, ct);

        var first = new DateOnly(Today.Year, Today.Month, 1).AddMonths(-5);
        var cogsId = await db.Accounts.AsNoTracking().Where(a => a.SystemRole == AccountRoles.CostOfGoodsSold).Select(a => (int?)a.Id).FirstOrDefaultAsync(ct);
        var revenueIds = await db.Accounts.AsNoTracking().Where(a => a.Type == AccountType.Revenue).Select(a => a.Id).ToListAsync(ct);

        var monthly = await (from l in db.JournalLines.AsNoTracking()
                             join e in db.JournalEntries.AsNoTracking() on l.JournalEntryId equals e.Id
                             where e.Status != JournalStatus.Draft && e.Date >= first
                             group l by new { e.Date.Year, e.Date.Month, l.AccountId } into g
                             select new { g.Key.Year, g.Key.Month, g.Key.AccountId, Net = g.Sum(x => x.Debit - x.Credit) }).ToListAsync(ct);

        var months = new List<MonthlyFigure>();
        for (var i = 0; i < 6; i++)
        {
            var m = first.AddMonths(i);
            var inMonth = monthly.Where(x => x.Year == m.Year && x.Month == m.Month).ToList();
            var revenue = -inMonth.Where(x => revenueIds.Contains(x.AccountId)).Sum(x => x.Net);
            var cogs = inMonth.Where(x => x.AccountId == cogsId).Sum(x => x.Net);
            months.Add(new MonthlyFigure($"{m.Year}-{m.Month:D2}", revenue, cogs, revenue - cogs));
        }
        var current = months[^1];
        return new AccountingSummary(cash, receivables, payables, inventory, current.Revenue, current.CostOfGoodsSold, current.GrossProfit, months, await GetLockDateAsync(ct));
    }

    // =====================================================================
    //  Settings
    // =====================================================================

    public async Task<DateOnly?> GetLockDateAsync(CancellationToken ct) =>
        await db.AccountingSettings.AsNoTracking().Select(s => s.LockDate).FirstOrDefaultAsync(ct);

    public async Task<DateOnly?> SetLockDateAsync(DateOnly? lockDate, CancellationToken ct)
    {
        var s = await db.AccountingSettings.FirstOrDefaultAsync(ct);
        if (s is null)
        {
            s = new AccountingSetting();
            db.AccountingSettings.Add(s);
        }
        s.LockDate = lockDate;
        s.UpdatedAt = DateTime.UtcNow;
        s.UpdatedBy = user.UserName;
        await SaveAsync(ct);
        return s.LockDate;
    }
}
