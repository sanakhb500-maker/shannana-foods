using Erp.Domain.Common;

namespace Erp.Domain.Entities;

public enum AccountType { Asset = 1, Liability = 2, Equity = 3, Revenue = 4, Expense = 5 }

/// <summary>Draft entries can still be edited; Posted entries count in the books; Reversed ones were undone by a reversal entry (both stay on the books and net to zero).</summary>
public enum JournalStatus { Draft = 1, Posted = 2, Reversed = 3 }

public enum PartyType { None = 0, Supplier = 1, Customer = 2 }

/// <summary>Chart-of-accounts node. Only postable (leaf) accounts take journal lines; header accounts group them.</summary>
public class Account : AuditableEntity
{
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string NameEn { get; set; } = "";
    public AccountType Type { get; set; }
    public int? ParentId { get; set; }
    public bool IsPostable { get; set; } = true;
    /// <summary>
    /// Links an account to an automatic posting rule (see <see cref="AccountRoles"/>). Set by the system only;
    /// clients cannot change it.
    /// </summary>
    public string? SystemRole { get; set; }
}

public class JournalEntry : AuditableEntity
{
    /// <summary>Assigned when the entry is posted (JV-000123), gap-free. A manual draft has none yet.</summary>
    public string? Number { get; set; }
    public DateOnly Date { get; set; }
    public string Description { get; set; } = "";
    /// <summary>"Manual" or the document type that produced the entry (see <see cref="SourceTypes"/>).</summary>
    public string SourceType { get; set; } = SourceTypes.Manual;
    public int? SourceId { get; set; }
    public JournalStatus Status { get; set; } = JournalStatus.Draft;
    /// <summary>For a reversal entry: the entry it undoes.</summary>
    public int? ReversalOfId { get; set; }
    public DateTime? PostedAt { get; set; }
    public string? PostedBy { get; set; }
    public List<JournalLine> Lines { get; set; } = [];
}

/// <summary>One side of an entry, in LOCAL currency. Exactly one of Debit / Credit is positive.</summary>
public class JournalLine
{
    public int Id { get; set; }
    public int JournalEntryId { get; set; }
    public int AccountId { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public string? Description { get; set; }
    /// <summary>Sub-ledger tag so payables/receivables can be reconciled per supplier / customer.</summary>
    public PartyType PartyType { get; set; } = PartyType.None;
    public int? PartyId { get; set; }
}

/// <summary>Single-row settings table (Id = 1).</summary>
public class AccountingSetting
{
    public int Id { get; set; } = 1;
    /// <summary>Nothing may be posted or reversed on or before this date.</summary>
    public DateOnly? LockDate { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>Role keys the automatic posting rules use to find accounts.</summary>
public static class AccountRoles
{
    public const string Cash = "Cash";
    public const string Bank = "Bank";
    public const string Receivables = "Receivables";
    public const string Inventory = "Inventory";
    public const string Payables = "Payables";
    /// <summary>Goods received but not yet invoiced (clearing account).</summary>
    public const string GoodsReceivedNotInvoiced = "GRNI";
    public const string OpeningEquity = "OpeningEquity";
    public const string SalesRevenue = "SalesRevenue";
    public const string SalesReturns = "SalesReturns";
    public const string CostOfGoodsSold = "COGS";
    public const string InventoryAdjustments = "InventoryAdjustments";

    public static readonly string[] All =
    [
        Cash, Bank, Receivables, Inventory, Payables, GoodsReceivedNotInvoiced, OpeningEquity,
        SalesRevenue, SalesReturns, CostOfGoodsSold, InventoryAdjustments,
    ];
}

public static class SourceTypes
{
    public const string Manual = "Manual";
    public const string GoodsReceipt = "GoodsReceipt";
    public const string PurchaseInvoice = "PurchaseInvoice";
    public const string SupplierPayment = "SupplierPayment";
    public const string DeliveryNote = "DeliveryNote";
    public const string SalesInvoice = "SalesInvoice";
    public const string SalesReturn = "SalesReturn";
    public const string CustomerReceipt = "CustomerReceipt";
    public const string StockAdjustment = "StockAdjustment";
}
