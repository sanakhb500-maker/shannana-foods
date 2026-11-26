using Erp.Application.Common;
using Erp.Domain.Entities;

namespace Erp.Application.Accounting;

// ---------- inputs ----------
public sealed record JournalLineInput(int AccountId, decimal Debit, decimal Credit, string? Description, PartyType PartyType = PartyType.None, int? PartyId = null);

public sealed record JournalEntryInput(DateOnly Date, string Description, List<JournalLineInput> Lines);

public sealed record LockDateInput(DateOnly? LockDate);

// ---------- filters ----------
public sealed record JournalFilter(JournalStatus? Status, string? SourceType, DateOnly? From, DateOnly? To, string? Q, int Page = 1, int PageSize = 20);

// ---------- read models ----------
public sealed record JournalRow(int Id, string Number, DateOnly Date, string Description, string SourceType, int? SourceId, JournalStatus Status, decimal Total);

/// <summary>Amounts are net debit (debit - credit): positive = debit balance, negative = credit balance.</summary>
public sealed record TrialBalanceRow(int AccountId, string Code, string NameAr, string NameEn, AccountType Type,
    decimal Opening, decimal Debit, decimal Credit, decimal Closing);

public sealed record TrialBalance(DateOnly? From, DateOnly? To, IReadOnlyList<TrialBalanceRow> Rows,
    decimal TotalDebit, decimal TotalCredit, decimal ClosingDebit, decimal ClosingCredit, bool IsBalanced);

public sealed record LedgerRow(DateOnly Date, int EntryId, string EntryNumber, string Description, string SourceType,
    decimal Debit, decimal Credit, decimal Balance, PartyType PartyType, int? PartyId);

public sealed record Ledger(int AccountId, string Code, string NameAr, string NameEn, AccountType Type, DateOnly? From, DateOnly? To,
    decimal Opening, IReadOnlyList<LedgerRow> Rows, decimal TotalDebit, decimal TotalCredit, decimal Closing);

public sealed record StatementLine(int AccountId, string Code, string NameAr, string NameEn, decimal Amount);

public sealed record IncomeStatement(DateOnly? From, DateOnly? To, IReadOnlyList<StatementLine> Revenue, decimal TotalRevenue,
    IReadOnlyList<StatementLine> Expenses, decimal TotalExpenses, decimal NetProfit);

/// <summary>Equity includes the profit earned so far (revenue - expenses up to the date) as its own line.</summary>
public sealed record BalanceSheet(DateOnly? AsOf, IReadOnlyList<StatementLine> Assets, decimal TotalAssets,
    IReadOnlyList<StatementLine> Liabilities, decimal TotalLiabilities, IReadOnlyList<StatementLine> Equity, decimal CurrentEarnings,
    decimal TotalEquity, bool IsBalanced);

/// <summary>
/// A control account in the general ledger compared with what the sub-ledger says. <see cref="Reconciled"/> allows a rounding
/// tolerance (stock is valued as quantity x a 4-decimal average cost, so it can drift by a fraction of a cent).
/// </summary>
public sealed record ReconciliationRow(string Key, decimal GeneralLedger, decimal SubLedger, decimal Difference, bool Reconciled);

public sealed record MonthlyFigure(string Month, decimal Revenue, decimal CostOfGoodsSold, decimal GrossProfit);

public sealed record AccountingSummary(decimal CashAndBank, decimal Receivables, decimal Payables, decimal InventoryValue,
    decimal MonthRevenue, decimal MonthCostOfGoodsSold, decimal MonthGrossProfit, IReadOnlyList<MonthlyFigure> Months, DateOnly? LockDate);

/// <summary>Accounting use cases for people: manual journals and reports. Automatic postings come from the document services.</summary>
public interface IAccountingService
{
    // journal
    Task<PagedResult<JournalRow>> ListEntriesAsync(JournalFilter f, CancellationToken ct);
    Task<JournalEntry> GetEntryAsync(int id, CancellationToken ct);
    Task<JournalEntry> CreateEntryAsync(JournalEntryInput input, CancellationToken ct);
    Task<JournalEntry> UpdateEntryAsync(int id, JournalEntryInput input, CancellationToken ct);
    Task<JournalEntry> PostEntryAsync(int id, CancellationToken ct);
    Task DeleteDraftAsync(int id, CancellationToken ct);
    Task<JournalEntry> ReverseEntryAsync(int id, CancellationToken ct);

    // reports
    Task<TrialBalance> GetTrialBalanceAsync(DateOnly? from, DateOnly? to, CancellationToken ct);
    Task<Ledger> GetLedgerAsync(int accountId, DateOnly? from, DateOnly? to, PartyType partyType, int? partyId, CancellationToken ct);
    Task<IncomeStatement> GetIncomeStatementAsync(DateOnly? from, DateOnly? to, CancellationToken ct);
    Task<BalanceSheet> GetBalanceSheetAsync(DateOnly? asOf, CancellationToken ct);
    Task<IReadOnlyList<ReconciliationRow>> GetReconciliationAsync(CancellationToken ct);
    Task<AccountingSummary> GetSummaryAsync(CancellationToken ct);

    // settings
    Task<DateOnly?> GetLockDateAsync(CancellationToken ct);
    Task<DateOnly?> SetLockDateAsync(DateOnly? lockDate, CancellationToken ct);
}
