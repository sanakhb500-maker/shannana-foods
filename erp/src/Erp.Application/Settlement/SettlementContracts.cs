namespace Erp.Application.Settlement;

/// <summary>Part of a payment / receipt assigned to one invoice.</summary>
/// <param name="Kind">PurchaseInvoice, ExpenseInvoice (supplier payments) or SalesInvoice (customer receipts).</param>
/// <param name="Amount">In the payment's currency, which must equal the invoice's currency.</param>
public sealed record AllocationInput(string Kind, int InvoiceId, decimal Amount);

public static class InvoiceKinds
{
    public const string PurchaseInvoice = "PurchaseInvoice";
    public const string ExpenseInvoice = "ExpenseInvoice";
    public const string SalesInvoice = "SalesInvoice";
}

/// <summary>A confirmed invoice that is not fully settled. Amounts are in the invoice's own currency except where noted.</summary>
/// <param name="Bucket">current / d1_30 / d31_60 / d61_90 / over90, by days past the due date (the invoice date when no due date).</param>
public sealed record OpenInvoiceRow(string Kind, int InvoiceId, string? Number, DateOnly Date, DateOnly? DueDate, int CurrencyId, decimal ExchangeRate,
    decimal Total, decimal Settled, decimal Outstanding, decimal OutstandingLocal, int DaysOverdue, string Bucket);

/// <summary>Outstanding per party by age, in LOCAL currency (each invoice at its own rate).</summary>
public sealed record AgeingRow(int PartyId, string Code, string NameAr, string NameEn,
    decimal Current, decimal Days1To30, decimal Days31To60, decimal Days61To90, decimal Over90, decimal Total);

public static class Ageing
{
    public static (int Days, string Bucket) Of(DateOnly due, DateOnly today)
    {
        var days = today.DayNumber - due.DayNumber;
        var bucket = days <= 0 ? "current" : days <= 30 ? "d1_30" : days <= 60 ? "d31_60" : days <= 90 ? "d61_90" : "over90";
        return (Math.Max(days, 0), bucket);
    }

    public static AgeingRow Row(int partyId, string code, string nameAr, string nameEn, IEnumerable<OpenInvoiceRow> rows)
    {
        var list = rows.ToList();
        decimal Sum(string b) => list.Where(r => r.Bucket == b).Sum(r => r.OutstandingLocal);
        return new AgeingRow(partyId, code, nameAr, nameEn, Sum("current"), Sum("d1_30"), Sum("d31_60"), Sum("d61_90"), Sum("over90"), list.Sum(r => r.OutstandingLocal));
    }
}
