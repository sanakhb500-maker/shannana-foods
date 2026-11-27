using Erp.Domain.Common;

namespace Erp.Domain.Entities;

// Non-stock spending. Lines name an EXPENSE ACCOUNT and an amount instead of an item and a quantity.

/// <summary>A supplier's bill for services (rent, electricity, repairs...). Confirming: Dr expense accounts / Cr Payables.</summary>
public class ExpenseInvoice : AuditableEntity
{
    public string? Number { get; set; }
    public DateOnly Date { get; set; }
    public DateOnly? DueDate { get; set; }
    public int SupplierId { get; set; }
    public string? SupplierInvoiceNumber { get; set; }
    public int CurrencyId { get; set; }
    public decimal ExchangeRate { get; set; } = 1m;
    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;
    public string? Notes { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public string? ConfirmedBy { get; set; }
    public List<ExpenseInvoiceLine> Lines { get; set; } = [];
}

public class ExpenseInvoiceLine
{
    public int Id { get; set; }
    public int ExpenseInvoiceId { get; set; }
    public int AccountId { get; set; }
    public string? Description { get; set; }
    public decimal Amount { get; set; }
}

/// <summary>Money spent immediately, with no supplier account. Confirming: Dr expense accounts / Cr Cash or Bank.</summary>
public class ExpenseVoucher : AuditableEntity
{
    public string? Number { get; set; }
    public DateOnly Date { get; set; }
    public int CurrencyId { get; set; }
    public decimal ExchangeRate { get; set; } = 1m;
    public PaymentMethod Method { get; set; } = PaymentMethod.Cash;
    /// <summary>Who was paid (free text).</summary>
    public string? Payee { get; set; }
    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;
    public string? Notes { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public string? ConfirmedBy { get; set; }
    public List<ExpenseVoucherLine> Lines { get; set; } = [];
}

public class ExpenseVoucherLine
{
    public int Id { get; set; }
    public int ExpenseVoucherId { get; set; }
    public int AccountId { get; set; }
    public string? Description { get; set; }
    public decimal Amount { get; set; }
}

/// <summary>Part of a supplier payment assigned to one bill (a purchase invoice or an expense invoice). Exactly one id is set.</summary>
public class SupplierPaymentAllocation
{
    public int Id { get; set; }
    public int SupplierPaymentId { get; set; }
    public int? PurchaseInvoiceId { get; set; }
    public int? ExpenseInvoiceId { get; set; }
    /// <summary>In the payment's (= the invoice's) currency.</summary>
    public decimal Amount { get; set; }
}

public class CustomerReceiptAllocation
{
    public int Id { get; set; }
    public int CustomerReceiptId { get; set; }
    public int SalesInvoiceId { get; set; }
    public decimal Amount { get; set; }
}

/// <summary>One revaluation of open foreign-currency invoices at a closing rate, posted as an entry that reverses itself the next day.</summary>
public class RevaluationRun : AuditableEntity
{
    public DateOnly AsOf { get; set; }
    public int? JournalEntryId { get; set; }
    public JournalEntry? JournalEntry { get; set; }
    public int? ReversalEntryId { get; set; }
    public JournalEntry? ReversalEntry { get; set; }
    /// <summary>Net unrealised exchange result posted (positive = gain).</summary>
    public decimal NetResult { get; set; }
}
