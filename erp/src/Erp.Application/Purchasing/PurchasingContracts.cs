using Erp.Application.Common;
using Erp.Domain.Entities;

namespace Erp.Application.Purchasing;

// ---------- inputs ----------
public sealed record PoLineInput(int ItemId, decimal Quantity, decimal UnitPrice);

/// <param name="ExchangeRate">Local units per 1 unit of the document currency. Empty = latest known rate on/before the date (1 for the local currency).</param>
public sealed record PurchaseOrderInput(DateOnly Date, DateOnly? ExpectedDate, int SupplierId, int CurrencyId, decimal? ExchangeRate,
    string? Notes, List<PoLineInput> Lines);

public sealed record ReceiptLineInput(int? PurchaseOrderLineId, int ItemId, decimal Quantity, decimal UnitPrice);

public sealed record GoodsReceiptInput(DateOnly Date, int SupplierId, int WarehouseId, int? PurchaseOrderId, int CurrencyId, decimal? ExchangeRate,
    string? Notes, List<ReceiptLineInput> Lines);

public sealed record InvoiceLineInput(int? PurchaseOrderLineId, int ItemId, decimal Quantity, decimal UnitPrice);

public sealed record PurchaseInvoiceInput(DateOnly Date, DateOnly? DueDate, int SupplierId, string? SupplierInvoiceNumber, int? PurchaseOrderId,
    int CurrencyId, decimal? ExchangeRate, string? Notes, List<InvoiceLineInput> Lines);

public sealed record SupplierPaymentInput(DateOnly Date, int SupplierId, int CurrencyId, decimal? ExchangeRate, decimal Amount,
    PaymentMethod Method, string? Reference, string? Notes);

// ---------- filters ----------
/// <param name="PendingOnly">Orders: only confirmed orders that still have quantity to receive.</param>
public sealed record PurchasingFilter(DocumentStatus? Status, int? SupplierId, string? Q, bool PendingOnly = false, int Page = 1, int PageSize = 20);

// ---------- read models ----------
public sealed record PurchaseOrderRow(int Id, string? Number, DateOnly Date, DateOnly? ExpectedDate, int SupplierId, int CurrencyId,
    DocumentStatus Status, decimal TotalAmount, ProgressState ReceiptState, ProgressState InvoiceState, int LineCount);

public sealed record GoodsReceiptRow(int Id, string? Number, DateOnly Date, int SupplierId, int WarehouseId, int? PurchaseOrderId, int CurrencyId,
    DocumentStatus Status, decimal TotalAmount, int LineCount);

public sealed record PurchaseInvoiceRow(int Id, string? Number, DateOnly Date, DateOnly? DueDate, int SupplierId, string? SupplierInvoiceNumber,
    int? PurchaseOrderId, int CurrencyId, DocumentStatus Status, decimal TotalAmount);

public sealed record StatementRow(DateOnly Date, string Type, string? Number, string? Reference, int CurrencyId, decimal Amount, decimal ExchangeRate,
    decimal Debit, decimal Credit, decimal Balance);

/// <summary>Supplier account in local currency: invoices raise the balance owed (credit), payments lower it (debit).</summary>
public sealed record SupplierStatement(int SupplierId, DateOnly? From, DateOnly? To, decimal OpeningBalance, IReadOnlyList<StatementRow> Rows,
    decimal TotalInvoiced, decimal TotalPaid, decimal ClosingBalance);

public sealed record SupplierBalanceRow(int SupplierId, string Code, string NameAr, string NameEn, decimal Invoiced, decimal Paid, decimal Balance);

/// <summary>Purchasing use cases. Confirm/cancel are atomic (document, stock and order progress commit together).</summary>
public interface IPurchasingService
{
    // purchase orders
    Task<PagedResult<PurchaseOrderRow>> ListOrdersAsync(PurchasingFilter f, CancellationToken ct);
    Task<PurchaseOrder> GetOrderAsync(int id, CancellationToken ct);
    Task<PurchaseOrder> CreateOrderAsync(PurchaseOrderInput input, CancellationToken ct);
    Task<PurchaseOrder> UpdateOrderAsync(int id, PurchaseOrderInput input, CancellationToken ct);
    Task<PurchaseOrder> ConfirmOrderAsync(int id, CancellationToken ct);
    Task<PurchaseOrder> CancelOrderAsync(int id, CancellationToken ct);

    // goods receipts
    Task<PagedResult<GoodsReceiptRow>> ListReceiptsAsync(PurchasingFilter f, CancellationToken ct);
    Task<GoodsReceipt> GetReceiptAsync(int id, CancellationToken ct);
    Task<GoodsReceipt> CreateReceiptAsync(GoodsReceiptInput input, CancellationToken ct);
    Task<GoodsReceipt> UpdateReceiptAsync(int id, GoodsReceiptInput input, CancellationToken ct);
    Task<GoodsReceipt> ConfirmReceiptAsync(int id, CancellationToken ct);
    Task<GoodsReceipt> CancelReceiptAsync(int id, CancellationToken ct);

    // purchase invoices
    Task<PagedResult<PurchaseInvoiceRow>> ListInvoicesAsync(PurchasingFilter f, CancellationToken ct);
    Task<PurchaseInvoice> GetInvoiceAsync(int id, CancellationToken ct);
    Task<PurchaseInvoice> CreateInvoiceAsync(PurchaseInvoiceInput input, CancellationToken ct);
    Task<PurchaseInvoice> UpdateInvoiceAsync(int id, PurchaseInvoiceInput input, CancellationToken ct);
    Task<PurchaseInvoice> ConfirmInvoiceAsync(int id, CancellationToken ct);
    Task<PurchaseInvoice> CancelInvoiceAsync(int id, CancellationToken ct);

    // supplier payments
    Task<PagedResult<SupplierPayment>> ListPaymentsAsync(PurchasingFilter f, CancellationToken ct);
    Task<SupplierPayment> GetPaymentAsync(int id, CancellationToken ct);
    Task<SupplierPayment> CreatePaymentAsync(SupplierPaymentInput input, CancellationToken ct);
    Task<SupplierPayment> UpdatePaymentAsync(int id, SupplierPaymentInput input, CancellationToken ct);
    Task<SupplierPayment> ConfirmPaymentAsync(int id, CancellationToken ct);
    Task<SupplierPayment> CancelPaymentAsync(int id, CancellationToken ct);

    // reports
    Task<SupplierStatement> GetStatementAsync(int supplierId, DateOnly? from, DateOnly? to, CancellationToken ct);
    Task<IReadOnlyList<SupplierBalanceRow>> GetSupplierBalancesAsync(CancellationToken ct);
}
