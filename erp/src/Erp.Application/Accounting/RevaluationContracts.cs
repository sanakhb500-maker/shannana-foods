namespace Erp.Application.Accounting;

public sealed record RevaluationInput(DateOnly AsOf);

/// <param name="IsReceivable">Customer invoice (true) or supplier bill (false).</param>
/// <param name="Difference">Local-currency effect of moving the outstanding amount from its booked rate to the closing rate
/// (positive = the balance grows: a gain on a receivable, a loss on a payable).</param>
public sealed record RevaluationLine(string Kind, int InvoiceId, string? Number, bool IsReceivable, int CurrencyId, decimal Outstanding,
    decimal BookedRate, decimal ClosingRate, decimal Difference);

public sealed record RevaluationPreview(DateOnly AsOf, IReadOnlyList<RevaluationLine> Lines, decimal ReceivablesDifference, decimal PayablesDifference,
    decimal NetResult);

public sealed record RevaluationRunRow(int Id, DateOnly AsOf, decimal NetResult, string? EntryNumber, string? ReversalNumber, DateTime CreatedAt, string? CreatedBy);

/// <summary>
/// Period-end revaluation of OPEN foreign-currency invoices at the closing rate. The result is posted on the as-of date and
/// reversed automatically the next day, so unrealised differences show in that period's reports but never compound; the realised
/// difference is booked when the invoice is actually paid.
/// </summary>
public interface IRevaluationService
{
    Task<RevaluationPreview> PreviewAsync(DateOnly asOf, CancellationToken ct);
    Task<RevaluationRunRow> RunAsync(DateOnly asOf, CancellationToken ct);
    Task<IReadOnlyList<RevaluationRunRow>> ListAsync(CancellationToken ct);
}
