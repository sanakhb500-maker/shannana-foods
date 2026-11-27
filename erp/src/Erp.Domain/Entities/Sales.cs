using Erp.Domain.Common;

namespace Erp.Domain.Entities;

public enum TripStatus { Planned = 1, InTransit = 2, Completed = 3, Cancelled = 4 }

public enum StopStatus { Pending = 1, Delivered = 2, Failed = 3 }

// Same money rules as purchasing: amounts are in the document currency, ExchangeRate is a snapshot
// (1 document-currency unit = ExchangeRate local units). Stock is valued in local currency.

public class SalesOrder : AuditableEntity
{
    public string? Number { get; set; }
    public DateOnly Date { get; set; }
    public DateOnly? RequiredDate { get; set; }
    public int CustomerId { get; set; }
    public int CurrencyId { get; set; }
    public decimal ExchangeRate { get; set; } = 1m;
    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;
    public string? Notes { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public string? ConfirmedBy { get; set; }
    public List<SalesOrderLine> Lines { get; set; } = [];
}

public class SalesOrderLine
{
    public int Id { get; set; }
    public int SalesOrderId { get; set; }
    public int ItemId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    /// <summary>Maintained by confirmed delivery notes.</summary>
    public decimal DeliveredQuantity { get; set; }
    /// <summary>Maintained by confirmed sales invoices.</summary>
    public decimal InvoicedQuantity { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>Goods leaving a warehouse for a customer. Confirming decreases stock.</summary>
public class DeliveryNote : AuditableEntity
{
    public string? Number { get; set; }
    public DateOnly Date { get; set; }
    public int CustomerId { get; set; }
    public int WarehouseId { get; set; }
    public int? SalesOrderId { get; set; }
    public int CurrencyId { get; set; }
    public decimal ExchangeRate { get; set; } = 1m;
    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;
    public string? Notes { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public string? ConfirmedBy { get; set; }
    public List<DeliveryNoteLine> Lines { get; set; } = [];
}

public class DeliveryNoteLine
{
    public int Id { get; set; }
    public int DeliveryNoteId { get; set; }
    public int? SalesOrderLineId { get; set; }
    public int ItemId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    /// <summary>Local-currency average cost the stock left at; filled on confirm (cost of goods sold, used by accounting later).</summary>
    public decimal? UnitCost { get; set; }
}

/// <summary>Customer bill. Confirming creates a receivable (no stock effect) and is checked against the customer's credit limit.</summary>
public class SalesInvoice : AuditableEntity
{
    public string? Number { get; set; }
    public DateOnly Date { get; set; }
    public DateOnly? DueDate { get; set; }
    public int CustomerId { get; set; }
    public int? SalesOrderId { get; set; }
    public int CurrencyId { get; set; }
    public decimal ExchangeRate { get; set; } = 1m;
    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;
    public string? Notes { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public string? ConfirmedBy { get; set; }
    public List<SalesInvoiceLine> Lines { get; set; } = [];
}

public class SalesInvoiceLine
{
    public int Id { get; set; }
    public int SalesInvoiceId { get; set; }
    public int? SalesOrderLineId { get; set; }
    public int ItemId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

/// <summary>Goods a customer sends back. Confirming increases stock and credits the customer's account.</summary>
public class SalesReturn : AuditableEntity
{
    public string? Number { get; set; }
    public DateOnly Date { get; set; }
    public int CustomerId { get; set; }
    public int WarehouseId { get; set; }
    /// <summary>Optional: the invoice these goods were sold on. A linked return cannot exceed what was invoiced and reduces that invoice's outstanding amount.</summary>
    public int? SalesInvoiceId { get; set; }
    public int CurrencyId { get; set; }
    public decimal ExchangeRate { get; set; } = 1m;
    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;
    /// <summary>Why the goods came back.</summary>
    public string? Notes { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public string? ConfirmedBy { get; set; }
    public List<SalesReturnLine> Lines { get; set; } = [];
}

public class SalesReturnLine
{
    public int Id { get; set; }
    public int SalesReturnId { get; set; }
    public int ItemId { get; set; }
    public decimal Quantity { get; set; }
    /// <summary>Credit price per unit, document currency.</summary>
    public decimal UnitPrice { get; set; }
    /// <summary>Local-currency cost the goods re-entered stock at; filled on confirm.</summary>
    public decimal? UnitCost { get; set; }
}

/// <summary>Money received from a customer, on account (not matched to a specific invoice).</summary>
public class CustomerReceipt : AuditableEntity
{
    public string? Number { get; set; }
    public DateOnly Date { get; set; }
    public int CustomerId { get; set; }
    public int CurrencyId { get; set; }
    public decimal ExchangeRate { get; set; } = 1m;
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; } = PaymentMethod.Cash;
    public string? Reference { get; set; }
    public string? Notes { get; set; }
    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;
    public DateTime? ConfirmedAt { get; set; }
    public string? ConfirmedBy { get; set; }
    /// <summary>Local amount that cleared the receivable when confirmed (allocated part at the invoice rate, the rest at this receipt's rate).</summary>
    public decimal? LocalSettled { get; set; }
    public List<CustomerReceiptAllocation> Allocations { get; set; } = [];
}

/// <summary>One delivery run: a driver and vehicle visiting a list of customers (stops = delivery notes) in order.</summary>
public class DistributionTrip : AuditableEntity
{
    public string? Number { get; set; }
    public DateOnly Date { get; set; }
    public int DriverId { get; set; }
    public int VehicleId { get; set; }
    public TripStatus Status { get; set; } = TripStatus.Planned;
    public string? Notes { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public List<TripStop> Stops { get; set; } = [];
}

public class TripStop
{
    public int Id { get; set; }
    public int TripId { get; set; }
    public int DeliveryNoteId { get; set; }
    /// <summary>Visit order, 1-based.</summary>
    public int Sequence { get; set; }
    public StopStatus Status { get; set; } = StopStatus.Pending;
    public string? Notes { get; set; }
}
