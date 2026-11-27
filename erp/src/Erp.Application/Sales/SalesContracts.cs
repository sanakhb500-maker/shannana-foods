using Erp.Application.Common;
using Erp.Application.Settlement;
using Erp.Domain.Entities;

namespace Erp.Application.Sales;

// ---------- inputs ----------
/// <param name="DiscountPercent">Line discount, 0-100.</param>
public sealed record SoLineInput(int ItemId, decimal Quantity, decimal UnitPrice, decimal DiscountPercent = 0);

/// <param name="ExchangeRate">Local units per 1 unit of the document currency. Empty = latest stored rate on/before the date (1 for the local currency).</param>
public sealed record SalesOrderInput(DateOnly Date, DateOnly? RequiredDate, int CustomerId, int CurrencyId, decimal? ExchangeRate,
    string? Notes, List<SoLineInput> Lines);

public sealed record DeliveryLineInput(int? SalesOrderLineId, int ItemId, decimal Quantity, decimal UnitPrice);

public sealed record DeliveryNoteInput(DateOnly Date, int CustomerId, int WarehouseId, int? SalesOrderId, int CurrencyId, decimal? ExchangeRate,
    string? Notes, List<DeliveryLineInput> Lines);

public sealed record SalesInvoiceLineInput(int? SalesOrderLineId, int ItemId, decimal Quantity, decimal UnitPrice, decimal DiscountPercent = 0);

/// <param name="DiscountAmount">Invoice-level discount in document currency, deducted from the sum of the discounted lines.</param>
public sealed record SalesInvoiceInput(DateOnly Date, DateOnly? DueDate, int CustomerId, int? SalesOrderId, int CurrencyId, decimal? ExchangeRate,
    string? Notes, List<SalesInvoiceLineInput> Lines, decimal DiscountAmount = 0);

public sealed record ReturnLineInput(int ItemId, decimal Quantity, decimal UnitPrice);

public sealed record SalesReturnInput(DateOnly Date, int CustomerId, int WarehouseId, int CurrencyId, decimal? ExchangeRate,
    string? Notes, List<ReturnLineInput> Lines, int? SalesInvoiceId = null);

public sealed record CustomerReceiptInput(DateOnly Date, int CustomerId, int CurrencyId, decimal? ExchangeRate, decimal Amount,
    PaymentMethod Method, string? Reference, string? Notes, List<AllocationInput>? Allocations = null);

/// <param name="DeliveryNoteIds">The stops, in visiting order.</param>
public sealed record TripInput(DateOnly Date, int DriverId, int VehicleId, string? Notes, List<int> DeliveryNoteIds);

public sealed record StopStatusInput(StopStatus Status, string? Notes);

// ---------- filters ----------
/// <param name="PendingOnly">Orders: confirmed orders that still have quantity to deliver.</param>
/// <param name="UnassignedOnly">Deliveries: not on any planned / in-transit trip.</param>
public sealed record SalesFilter(DocumentStatus? Status, int? CustomerId, string? Q, bool PendingOnly = false, bool UnassignedOnly = false,
    int Page = 1, int PageSize = 20);

public sealed record TripFilter(TripStatus? Status, int? DriverId, int? VehicleId, DateOnly? From, DateOnly? To, int Page = 1, int PageSize = 20);

// ---------- read models ----------
public sealed record SalesOrderRow(int Id, string? Number, DateOnly Date, DateOnly? RequiredDate, int CustomerId, int CurrencyId,
    DocumentStatus Status, decimal TotalAmount, ProgressState DeliveryState, ProgressState InvoiceState, int LineCount);

public sealed record DeliveryNoteRow(int Id, string? Number, DateOnly Date, int CustomerId, int WarehouseId, int? SalesOrderId, int CurrencyId,
    DocumentStatus Status, decimal TotalAmount, int LineCount, string? TripNumber);

public sealed record SalesInvoiceRow(int Id, string? Number, DateOnly Date, DateOnly? DueDate, int CustomerId, int? SalesOrderId, int CurrencyId,
    DocumentStatus Status, decimal TotalAmount);

public sealed record SalesReturnRow(int Id, string? Number, DateOnly Date, int CustomerId, int WarehouseId, int? SalesInvoiceId, int CurrencyId,
    DocumentStatus Status, decimal TotalAmount, int LineCount);

public sealed record TripRow(int Id, string? Number, DateOnly Date, int DriverId, int VehicleId, TripStatus Status, int StopCount, int DeliveredCount);

public sealed record StopView(int Id, int Sequence, StopStatus Status, string? Notes, int DeliveryNoteId, string? DeliveryNumber,
    int CustomerId, int WarehouseId, int CurrencyId, decimal TotalAmount, DocumentStatus DeliveryStatus);

/// <param name="TotalWeightKg">Weight of the goods on the trip's live deliveries (quantity x item weight).</param>
/// <param name="CapacityKg">The vehicle's capacity (0 = not set).</param>
/// <param name="OverCapacity">A warning, not a block: the load exceeds the vehicle's capacity.</param>
/// <param name="LinesWithoutWeight">Delivery lines whose item has no weight, so the total may be understated.</param>
public sealed record TripDetail(int Id, string? Number, DateOnly Date, int DriverId, int VehicleId, TripStatus Status, string? Notes,
    DateTime? StartedAt, DateTime? CompletedAt, IReadOnlyList<StopView> Stops,
    decimal TotalWeightKg = 0, decimal CapacityKg = 0, bool OverCapacity = false, int LinesWithoutWeight = 0);

public sealed record CustomerStatementRow(DateOnly Date, string Type, string? Number, string? Reference, int CurrencyId, decimal Amount,
    decimal ExchangeRate, decimal Debit, decimal Credit, decimal Balance);

/// <summary>Customer account in local currency: invoices raise what they owe (debit); returns and receipts lower it (credit).</summary>
public sealed record CustomerStatement(int CustomerId, DateOnly? From, DateOnly? To, decimal OpeningBalance, IReadOnlyList<CustomerStatementRow> Rows,
    decimal TotalInvoiced, decimal TotalCredited, decimal ClosingBalance, decimal CreditLimit);

public sealed record CustomerBalanceRow(int CustomerId, string Code, string NameAr, string NameEn, decimal Invoiced, decimal Returned,
    decimal Received, decimal Balance, decimal CreditLimit);

/// <summary>Sales use cases. Confirm/cancel are atomic (document, stock and order progress commit together).</summary>
public interface ISalesService
{
    // sales orders
    Task<PagedResult<SalesOrderRow>> ListOrdersAsync(SalesFilter f, CancellationToken ct);
    Task<SalesOrder> GetOrderAsync(int id, CancellationToken ct);
    Task<SalesOrder> CreateOrderAsync(SalesOrderInput input, CancellationToken ct);
    Task<SalesOrder> UpdateOrderAsync(int id, SalesOrderInput input, CancellationToken ct);
    Task<SalesOrder> ConfirmOrderAsync(int id, CancellationToken ct);
    Task<SalesOrder> CancelOrderAsync(int id, CancellationToken ct);

    // delivery notes
    Task<PagedResult<DeliveryNoteRow>> ListDeliveriesAsync(SalesFilter f, CancellationToken ct);
    Task<DeliveryNote> GetDeliveryAsync(int id, CancellationToken ct);
    Task<DeliveryNote> CreateDeliveryAsync(DeliveryNoteInput input, CancellationToken ct);
    Task<DeliveryNote> UpdateDeliveryAsync(int id, DeliveryNoteInput input, CancellationToken ct);
    Task<DeliveryNote> ConfirmDeliveryAsync(int id, CancellationToken ct);
    Task<DeliveryNote> CancelDeliveryAsync(int id, CancellationToken ct);

    // sales invoices
    Task<PagedResult<SalesInvoiceRow>> ListInvoicesAsync(SalesFilter f, CancellationToken ct);
    Task<SalesInvoice> GetInvoiceAsync(int id, CancellationToken ct);
    Task<SalesInvoice> CreateInvoiceAsync(SalesInvoiceInput input, CancellationToken ct);
    Task<SalesInvoice> UpdateInvoiceAsync(int id, SalesInvoiceInput input, CancellationToken ct);
    Task<SalesInvoice> ConfirmInvoiceAsync(int id, CancellationToken ct);
    Task<SalesInvoice> CancelInvoiceAsync(int id, CancellationToken ct);

    // sales returns
    Task<PagedResult<SalesReturnRow>> ListReturnsAsync(SalesFilter f, CancellationToken ct);
    Task<SalesReturn> GetReturnAsync(int id, CancellationToken ct);
    Task<SalesReturn> CreateReturnAsync(SalesReturnInput input, CancellationToken ct);
    Task<SalesReturn> UpdateReturnAsync(int id, SalesReturnInput input, CancellationToken ct);
    Task<SalesReturn> ConfirmReturnAsync(int id, CancellationToken ct);
    Task<SalesReturn> CancelReturnAsync(int id, CancellationToken ct);

    // customer receipts
    Task<PagedResult<CustomerReceipt>> ListReceiptsAsync(SalesFilter f, CancellationToken ct);
    Task<CustomerReceipt> GetReceiptAsync(int id, CancellationToken ct);
    Task<CustomerReceipt> CreateReceiptAsync(CustomerReceiptInput input, CancellationToken ct);
    Task<CustomerReceipt> UpdateReceiptAsync(int id, CustomerReceiptInput input, CancellationToken ct);
    Task<CustomerReceipt> ConfirmReceiptAsync(int id, CancellationToken ct);
    Task<CustomerReceipt> CancelReceiptAsync(int id, CancellationToken ct);

    // distribution trips
    Task<PagedResult<TripRow>> ListTripsAsync(TripFilter f, CancellationToken ct);
    Task<TripDetail> GetTripAsync(int id, CancellationToken ct);
    Task<TripDetail> CreateTripAsync(TripInput input, CancellationToken ct);
    Task<TripDetail> UpdateTripAsync(int id, TripInput input, CancellationToken ct);
    Task<TripDetail> StartTripAsync(int id, CancellationToken ct);
    Task<TripDetail> CompleteTripAsync(int id, CancellationToken ct);
    Task<TripDetail> CancelTripAsync(int id, CancellationToken ct);
    Task<TripDetail> SetStopStatusAsync(int tripId, int stopId, StopStatusInput input, CancellationToken ct);

    // reports
    Task<CustomerStatement> GetStatementAsync(int customerId, DateOnly? from, DateOnly? to, CancellationToken ct);
    Task<IReadOnlyList<CustomerBalanceRow>> GetCustomerBalancesAsync(CancellationToken ct);
    Task<IReadOnlyList<OpenInvoiceRow>> GetOpenInvoicesAsync(int? customerId, CancellationToken ct);
    Task<IReadOnlyList<AgeingRow>> GetAgeingAsync(CancellationToken ct);
}
