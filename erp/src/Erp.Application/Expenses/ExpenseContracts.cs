using Erp.Application.Common;
using Erp.Domain.Entities;
using FluentValidation;

namespace Erp.Application.Expenses;

// ---------- inputs ----------
public sealed record ExpenseLineInput(int AccountId, string? Description, decimal Amount);

public sealed record ExpenseInvoiceInput(DateOnly Date, DateOnly? DueDate, int SupplierId, string? SupplierInvoiceNumber, int CurrencyId, decimal? ExchangeRate,
    string? Notes, List<ExpenseLineInput> Lines);

public sealed record ExpenseVoucherInput(DateOnly Date, int CurrencyId, decimal? ExchangeRate, PaymentMethod Method, string? Payee, string? Notes,
    List<ExpenseLineInput> Lines);

public sealed record ExpenseFilter(DocumentStatus? Status, int? SupplierId, string? Q, int Page = 1, int PageSize = 20);

// ---------- read models ----------
public sealed record ExpenseInvoiceRow(int Id, string? Number, DateOnly Date, DateOnly? DueDate, int SupplierId, string? SupplierInvoiceNumber,
    int CurrencyId, DocumentStatus Status, decimal TotalAmount);

public sealed record ExpenseVoucherRow(int Id, string? Number, DateOnly Date, string? Payee, PaymentMethod Method, int CurrencyId, DocumentStatus Status, decimal TotalAmount);

/// <summary>Non-stock spending: supplier bills for services, and cash/bank expense vouchers. Confirm/cancel post to the ledger atomically.</summary>
public interface IExpenseService
{
    Task<PagedResult<ExpenseInvoiceRow>> ListInvoicesAsync(ExpenseFilter f, CancellationToken ct);
    Task<ExpenseInvoice> GetInvoiceAsync(int id, CancellationToken ct);
    Task<ExpenseInvoice> CreateInvoiceAsync(ExpenseInvoiceInput input, CancellationToken ct);
    Task<ExpenseInvoice> UpdateInvoiceAsync(int id, ExpenseInvoiceInput input, CancellationToken ct);
    Task<ExpenseInvoice> ConfirmInvoiceAsync(int id, CancellationToken ct);
    Task<ExpenseInvoice> CancelInvoiceAsync(int id, CancellationToken ct);

    Task<PagedResult<ExpenseVoucherRow>> ListVouchersAsync(ExpenseFilter f, CancellationToken ct);
    Task<ExpenseVoucher> GetVoucherAsync(int id, CancellationToken ct);
    Task<ExpenseVoucher> CreateVoucherAsync(ExpenseVoucherInput input, CancellationToken ct);
    Task<ExpenseVoucher> UpdateVoucherAsync(int id, ExpenseVoucherInput input, CancellationToken ct);
    Task<ExpenseVoucher> ConfirmVoucherAsync(int id, CancellationToken ct);
    Task<ExpenseVoucher> CancelVoucherAsync(int id, CancellationToken ct);
}

public class ExpenseInvoiceInputValidator : AbstractValidator<ExpenseInvoiceInput>
{
    public ExpenseInvoiceInputValidator()
    {
        RuleFor(x => x.Date).NotEqual(default(DateOnly));
        RuleFor(x => x.DueDate).GreaterThanOrEqualTo(x => x.Date).When(x => x.DueDate.HasValue).WithMessage("Due date cannot be before the invoice date.");
        RuleFor(x => x.SupplierId).GreaterThan(0);
        RuleFor(x => x.CurrencyId).GreaterThan(0);
        RuleFor(x => x.ExchangeRate).GreaterThan(0).When(x => x.ExchangeRate.HasValue);
        RuleFor(x => x.SupplierInvoiceNumber).MaximumLength(50);
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one line is required.");
        RuleForEach(x => x.Lines).SetValidator(new ExpenseLineValidator());
    }
}

public class ExpenseVoucherInputValidator : AbstractValidator<ExpenseVoucherInput>
{
    public ExpenseVoucherInputValidator()
    {
        RuleFor(x => x.Date).NotEqual(default(DateOnly));
        RuleFor(x => x.CurrencyId).GreaterThan(0);
        RuleFor(x => x.ExchangeRate).GreaterThan(0).When(x => x.ExchangeRate.HasValue);
        RuleFor(x => x.Method).IsInEnum();
        RuleFor(x => x.Payee).MaximumLength(200);
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one line is required.");
        RuleForEach(x => x.Lines).SetValidator(new ExpenseLineValidator());
    }
}

public class ExpenseLineValidator : AbstractValidator<ExpenseLineInput>
{
    public ExpenseLineValidator()
    {
        RuleFor(x => x.AccountId).GreaterThan(0);
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Description).MaximumLength(200);
    }
}
