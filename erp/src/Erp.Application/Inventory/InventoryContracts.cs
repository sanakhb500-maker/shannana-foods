using Erp.Application.Common;
using Erp.Domain.Entities;

namespace Erp.Application.Inventory;

// ---------- inputs ----------
public sealed record TransferLineInput(int ItemId, decimal Quantity);

public sealed record TransferInput(DateOnly Date, int FromWarehouseId, int ToWarehouseId, string? Notes, List<TransferLineInput> Lines);

public sealed record AdjustmentLineInput(int ItemId, decimal CountedQuantity, decimal? UnitCost);

public sealed record AdjustmentInput(DateOnly Date, int WarehouseId, AdjustmentReason Reason, string? Notes, List<AdjustmentLineInput> Lines);

// ---------- filters ----------
public sealed record DocumentFilter(DocumentStatus? Status, int? WarehouseId, string? Q, int Page = 1, int PageSize = 20);

public sealed record BalanceFilter(int? WarehouseId, int? ItemId, string? Q, bool NonZeroOnly = true, int Page = 1, int PageSize = 50);

public sealed record MovementFilter(int? WarehouseId, int? ItemId, DateOnly? From, DateOnly? To, string? DocumentType, int Page = 1, int PageSize = 50);

// ---------- read models ----------
public sealed record BalanceRow(
    int ItemId, string ItemCode, string ItemNameAr, string ItemNameEn,
    int WarehouseId, string WarehouseCode, string WarehouseNameAr, string WarehouseNameEn,
    decimal Quantity, decimal AverageCost, decimal Value);

public sealed record BalancesResult(IReadOnlyList<BalanceRow> Items, int Total, int Page, int PageSize, decimal TotalValue);

public sealed record MovementRow(
    int Id, DateOnly Date, int ItemId, string ItemCode, string ItemNameAr, string ItemNameEn,
    int WarehouseId, string WarehouseCode, string WarehouseNameAr, string WarehouseNameEn,
    decimal Quantity, decimal UnitCost, decimal Value, MovementType Type,
    string DocumentType, int DocumentId, string? DocumentNumber, DateTime CreatedAt, string? CreatedBy);

public sealed record TransferRow(int Id, string? Number, DateOnly Date, int FromWarehouseId, int ToWarehouseId, DocumentStatus Status, string? Notes, int LineCount, DateTime CreatedAt, string? CreatedBy);

public sealed record AdjustmentRow(int Id, string? Number, DateOnly Date, int WarehouseId, AdjustmentReason Reason, DocumentStatus Status, string? Notes, int LineCount, DateTime CreatedAt, string? CreatedBy);

public sealed record LowStockRow(int ItemId, string ItemCode, string ItemNameAr, string ItemNameEn, decimal ReorderLevel, decimal TotalQuantity);

/// <summary>Stock management use cases. Posting (confirm/cancel) is atomic: movements, balances and status commit together or not at all.</summary>
public interface IInventoryService
{
    // transfers
    Task<PagedResult<TransferRow>> ListTransfersAsync(DocumentFilter f, CancellationToken ct);
    Task<StockTransfer> GetTransferAsync(int id, CancellationToken ct);
    Task<StockTransfer> CreateTransferAsync(TransferInput input, CancellationToken ct);
    Task<StockTransfer> UpdateTransferAsync(int id, TransferInput input, CancellationToken ct);
    Task<StockTransfer> ConfirmTransferAsync(int id, CancellationToken ct);
    Task<StockTransfer> CancelTransferAsync(int id, CancellationToken ct);

    // adjustments
    Task<PagedResult<AdjustmentRow>> ListAdjustmentsAsync(DocumentFilter f, CancellationToken ct);
    Task<StockAdjustment> GetAdjustmentAsync(int id, CancellationToken ct);
    Task<StockAdjustment> CreateAdjustmentAsync(AdjustmentInput input, CancellationToken ct);
    Task<StockAdjustment> UpdateAdjustmentAsync(int id, AdjustmentInput input, CancellationToken ct);
    Task<StockAdjustment> ConfirmAdjustmentAsync(int id, CancellationToken ct);
    Task<StockAdjustment> CancelAdjustmentAsync(int id, CancellationToken ct);

    // reports
    Task<BalancesResult> GetBalancesAsync(BalanceFilter f, CancellationToken ct);
    Task<PagedResult<MovementRow>> GetMovementsAsync(MovementFilter f, CancellationToken ct);
    Task<IReadOnlyList<LowStockRow>> GetLowStockAsync(CancellationToken ct);
}
