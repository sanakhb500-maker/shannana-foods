namespace Erp.Domain.Entities;

/// <summary>
/// Last number handed out for one document family. Numbers are assigned when a document is CONFIRMED, inside the
/// same transaction, so the series has no gaps: a draft has no number, and a failed confirm rolls the counter back.
/// </summary>
public class DocumentSequence
{
    /// <summary>The prefix, e.g. "PO".</summary>
    public string Key { get; set; } = "";
    public int LastNumber { get; set; }
    /// <summary>Two people confirming at once cannot receive the same number: the loser retries.</summary>
    public byte[] RowVersion { get; set; } = [];
}

public static class SequenceKeys
{
    public const string Transfer = "TRF";
    public const string Adjustment = "ADJ";
    public const string PurchaseOrder = "PO";
    public const string GoodsReceipt = "GRN";
    public const string PurchaseInvoice = "PINV";
    public const string SupplierPayment = "PAY";
    public const string SalesOrder = "SO";
    public const string Delivery = "DLV";
    public const string SalesInvoice = "SINV";
    public const string SalesReturn = "SRN";
    public const string CustomerReceipt = "RCT";
    public const string Journal = "JV";

    public static readonly string[] All =
    [
        Transfer, Adjustment, PurchaseOrder, GoodsReceipt, PurchaseInvoice, SupplierPayment,
        SalesOrder, Delivery, SalesInvoice, SalesReturn, CustomerReceipt, Journal,
    ];
}
