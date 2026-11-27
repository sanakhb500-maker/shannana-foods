using Erp.Domain.Common;

namespace Erp.Domain.Entities;

public enum DocumentStatus { Draft = 1, Confirmed = 2, Cancelled = 3 }

public enum AdjustmentReason { Count = 1, Opening = 2, Damage = 3, Other = 4 }

public enum MovementType
{
    TransferOut = 1,
    TransferIn = 2,
    AdjustmentOut = 3,
    AdjustmentIn = 4,
    /// <summary>Undoes a previously confirmed document's movement.</summary>
    Reversal = 9,
    PurchaseReceipt = 10,
    SalesDelivery = 11,
    SalesReturn = 12,
    PurchaseReturn = 13,
}

/// <summary>Document-type keys stored on movements (kept as text so later phases can add their own).</summary>
public static class DocumentTypes
{
    public const string Transfer = "StockTransfer";
    public const string Adjustment = "StockAdjustment";
    public const string GoodsReceipt = "GoodsReceipt";
    public const string Delivery = "DeliveryNote";
    public const string SalesReturn = "SalesReturn";
    public const string PurchaseReturn = "PurchaseReturn";
}

/// <summary>Current quantity and moving-average cost of one item in one warehouse. Maintained together with <see cref="StockMovement"/>.</summary>
public class StockBalance
{
    public int Id { get; set; }
    public int ItemId { get; set; }
    public int WarehouseId { get; set; }
    public decimal Quantity { get; set; }
    /// <summary>Moving-average unit cost in local currency.</summary>
    public decimal AverageCost { get; set; }
    public DateTime? LastMovementAt { get; set; }
    /// <summary>Optimistic concurrency: two simultaneous postings to the same balance cannot both win.</summary>
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>Append-only stock ledger. Quantity is signed (+in / -out). Never edited or deleted.</summary>
public class StockMovement
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    public int ItemId { get; set; }
    public int WarehouseId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public MovementType Type { get; set; }
    public string DocumentType { get; set; } = "";
    public int DocumentId { get; set; }
    public string? DocumentNumber { get; set; }
    /// <summary>For reversal movements: the movement being undone.</summary>
    public int? ReversalOfId { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
}

public class StockTransfer : AuditableEntity
{
    public string? Number { get; set; }
    public DateOnly Date { get; set; }
    public int FromWarehouseId { get; set; }
    public int ToWarehouseId { get; set; }
    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;
    public string? Notes { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public string? ConfirmedBy { get; set; }
    public List<StockTransferLine> Lines { get; set; } = [];
}

public class StockTransferLine
{
    public int Id { get; set; }
    public int StockTransferId { get; set; }
    public int ItemId { get; set; }
    public decimal Quantity { get; set; }
}

/// <summary>Stock count / opening balance / write-off. Lines state the counted quantity; the difference is posted on confirm.</summary>
public class StockAdjustment : AuditableEntity
{
    public string? Number { get; set; }
    public DateOnly Date { get; set; }
    public int WarehouseId { get; set; }
    public AdjustmentReason Reason { get; set; } = AdjustmentReason.Count;
    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;
    public string? Notes { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public string? ConfirmedBy { get; set; }
    public List<StockAdjustmentLine> Lines { get; set; } = [];
}

public class StockAdjustmentLine
{
    public int Id { get; set; }
    public int StockAdjustmentId { get; set; }
    public int ItemId { get; set; }
    public decimal CountedQuantity { get; set; }
    /// <summary>Cost for surplus quantity. Empty = current average cost, or the item's standard cost if none.</summary>
    public decimal? UnitCost { get; set; }
    /// <summary>Filled when confirmed: system quantity at that moment and the posted difference.</summary>
    public decimal? SystemQuantity { get; set; }
    public decimal? Difference { get; set; }
}
