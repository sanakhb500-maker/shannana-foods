using Erp.Domain.Common;

namespace Erp.Domain.Entities;

public enum PaymentMethod { Cash = 1, BankTransfer = 2, Cheque = 3 }

/// <summary>How far an order has been received / invoiced (derived from its lines).</summary>
public enum ProgressState { None = 0, Partial = 1, Full = 2 }

// Amounts on purchasing documents are in the document currency. ExchangeRate is a snapshot taken at
// save time: 1 unit of the document currency = ExchangeRate units of the local currency (1 for local).
// Stock is always valued in local currency (unit price x rate).

public class PurchaseOrder : AuditableEntity
{
    public string? Number { get; set; }
    public DateOnly Date { get; set; }
    public DateOnly? ExpectedDate { get; set; }
    public int SupplierId { get; set; }
    public int CurrencyId { get; set; }
    public decimal ExchangeRate { get; set; } = 1m;
    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;
    public string? Notes { get; set; }
    /// <summary>Sum of line amounts, document currency.</summary>
    public decimal TotalAmount { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public string? ConfirmedBy { get; set; }
    public List<PurchaseOrderLine> Lines { get; set; } = [];
}

public class PurchaseOrderLine
{
    public int Id { get; set; }
    public int PurchaseOrderId { get; set; }
    public int ItemId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    /// <summary>Maintained by confirmed goods receipts.</summary>
    public decimal ReceivedQuantity { get; set; }
    /// <summary>Maintained by confirmed purchase invoices.</summary>
    public decimal InvoicedQuantity { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>Goods received into a warehouse (against an order, or direct). Confirming increases stock.</summary>
public class GoodsReceipt : AuditableEntity
{
    public string? Number { get; set; }
    public DateOnly Date { get; set; }
    public int SupplierId { get; set; }
    public int WarehouseId { get; set; }
    public int? PurchaseOrderId { get; set; }
    public int CurrencyId { get; set; }
    public decimal ExchangeRate { get; set; } = 1m;
    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;
    public string? Notes { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public string? ConfirmedBy { get; set; }
    public List<GoodsReceiptLine> Lines { get; set; } = [];
}

public class GoodsReceiptLine
{
    public int Id { get; set; }
    public int GoodsReceiptId { get; set; }
    public int? PurchaseOrderLineId { get; set; }
    public int ItemId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

/// <summary>Supplier's bill. Confirming creates a payable (no stock effect).</summary>
public class PurchaseInvoice : AuditableEntity
{
    public string? Number { get; set; }
    public DateOnly Date { get; set; }
    public DateOnly? DueDate { get; set; }
    public int SupplierId { get; set; }
    /// <summary>The number printed on the supplier's own invoice.</summary>
    public string? SupplierInvoiceNumber { get; set; }
    public int? PurchaseOrderId { get; set; }
    public int CurrencyId { get; set; }
    public decimal ExchangeRate { get; set; } = 1m;
    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;
    public string? Notes { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public string? ConfirmedBy { get; set; }
    public List<PurchaseInvoiceLine> Lines { get; set; } = [];
}

public class PurchaseInvoiceLine
{
    public int Id { get; set; }
    public int PurchaseInvoiceId { get; set; }
    public int? PurchaseOrderLineId { get; set; }
    public int ItemId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

/// <summary>Money paid to a supplier, on account (not allocated to a specific invoice).</summary>
public class SupplierPayment : AuditableEntity
{
    public string? Number { get; set; }
    public DateOnly Date { get; set; }
    public int SupplierId { get; set; }
    public int CurrencyId { get; set; }
    public decimal ExchangeRate { get; set; } = 1m;
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; } = PaymentMethod.Cash;
    public string? Reference { get; set; }
    public string? Notes { get; set; }
    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;
    public DateTime? ConfirmedAt { get; set; }
    public string? ConfirmedBy { get; set; }
    /// <summary>Local amount that cleared the payable when confirmed (allocated part at the invoice rate, the rest at this payment's rate).</summary>
    public decimal? LocalSettled { get; set; }
    public List<SupplierPaymentAllocation> Allocations { get; set; } = [];
}

/// <summary>Goods sent back to a supplier. Confirming takes stock out and reduces what we owe (Dr Payables, Cr Inventory; any price/cost difference goes to GRNI).</summary>
public class PurchaseReturn : AuditableEntity
{
    public string? Number { get; set; }
    public DateOnly Date { get; set; }
    public int SupplierId { get; set; }
    public int WarehouseId { get; set; }
    /// <summary>Optional: the invoice these goods were bought on. A linked return cannot exceed what was invoiced and reduces that invoice's outstanding amount.</summary>
    public int? PurchaseInvoiceId { get; set; }
    public int CurrencyId { get; set; }
    public decimal ExchangeRate { get; set; } = 1m;
    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;
    public string? Notes { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public string? ConfirmedBy { get; set; }
    public List<PurchaseReturnLine> Lines { get; set; } = [];
}

public class PurchaseReturnLine
{
    public int Id { get; set; }
    public int PurchaseReturnId { get; set; }
    public int ItemId { get; set; }
    public decimal Quantity { get; set; }
    /// <summary>Credit price per unit, document currency.</summary>
    public decimal UnitPrice { get; set; }
    /// <summary>Local-currency average cost the stock left at; filled on confirm.</summary>
    public decimal? UnitCost { get; set; }
}
