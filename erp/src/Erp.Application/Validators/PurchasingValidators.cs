using Erp.Application.Purchasing;
using FluentValidation;

namespace Erp.Application.Validators;

public class PurchaseOrderInputValidator : AbstractValidator<PurchaseOrderInput>
{
    public PurchaseOrderInputValidator()
    {
        RuleFor(x => x.Date).NotEqual(default(DateOnly));
        RuleFor(x => x.SupplierId).GreaterThan(0);
        RuleFor(x => x.CurrencyId).GreaterThan(0);
        RuleFor(x => x.ExchangeRate).GreaterThan(0).When(x => x.ExchangeRate.HasValue);
        RuleFor(x => x.ExpectedDate).GreaterThanOrEqualTo(x => x.Date).When(x => x.ExpectedDate.HasValue)
            .WithMessage("Expected date cannot be before the order date.");
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one line is required.");
        RuleFor(x => x.Lines).Must(l => l.Select(i => i.ItemId).Distinct().Count() == l.Count)
            .When(x => x.Lines is not null).WithMessage("An item may appear only once per order.");
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(i => i.ItemId).GreaterThan(0);
            l.RuleFor(i => i.Quantity).GreaterThan(0);
            l.RuleFor(i => i.UnitPrice).GreaterThanOrEqualTo(0);
        });
    }
}

public class GoodsReceiptInputValidator : AbstractValidator<GoodsReceiptInput>
{
    public GoodsReceiptInputValidator()
    {
        RuleFor(x => x.Date).NotEqual(default(DateOnly));
        RuleFor(x => x.SupplierId).GreaterThan(0);
        RuleFor(x => x.WarehouseId).GreaterThan(0);
        RuleFor(x => x.CurrencyId).GreaterThan(0);
        RuleFor(x => x.ExchangeRate).GreaterThan(0).When(x => x.ExchangeRate.HasValue);
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one line is required.");
        RuleFor(x => x.Lines).Must(l => l.Select(i => i.ItemId).Distinct().Count() == l.Count)
            .When(x => x.Lines is not null).WithMessage("An item may appear only once per receipt.");
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(i => i.ItemId).GreaterThan(0);
            l.RuleFor(i => i.Quantity).GreaterThan(0);
            l.RuleFor(i => i.UnitPrice).GreaterThanOrEqualTo(0);
        });
    }
}

public class PurchaseInvoiceInputValidator : AbstractValidator<PurchaseInvoiceInput>
{
    public PurchaseInvoiceInputValidator()
    {
        RuleFor(x => x.Date).NotEqual(default(DateOnly));
        RuleFor(x => x.DueDate).GreaterThanOrEqualTo(x => x.Date).When(x => x.DueDate.HasValue)
            .WithMessage("Due date cannot be before the invoice date.");
        RuleFor(x => x.SupplierId).GreaterThan(0);
        RuleFor(x => x.CurrencyId).GreaterThan(0);
        RuleFor(x => x.ExchangeRate).GreaterThan(0).When(x => x.ExchangeRate.HasValue);
        RuleFor(x => x.SupplierInvoiceNumber).MaximumLength(50);
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one line is required.");
        RuleFor(x => x.Lines).Must(l => l.Select(i => i.ItemId).Distinct().Count() == l.Count)
            .When(x => x.Lines is not null).WithMessage("An item may appear only once per invoice.");
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(i => i.ItemId).GreaterThan(0);
            l.RuleFor(i => i.Quantity).GreaterThan(0);
            l.RuleFor(i => i.UnitPrice).GreaterThanOrEqualTo(0);
        });
    }
}

public class SupplierPaymentInputValidator : AbstractValidator<SupplierPaymentInput>
{
    public SupplierPaymentInputValidator()
    {
        RuleFor(x => x.Date).NotEqual(default(DateOnly));
        RuleFor(x => x.SupplierId).GreaterThan(0);
        RuleFor(x => x.CurrencyId).GreaterThan(0);
        RuleFor(x => x.ExchangeRate).GreaterThan(0).When(x => x.ExchangeRate.HasValue);
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Method).IsInEnum();
        RuleFor(x => x.Reference).MaximumLength(100);
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

public class PurchaseReturnInputValidator : AbstractValidator<PurchaseReturnInput>
{
    public PurchaseReturnInputValidator()
    {
        RuleFor(x => x.Date).NotEqual(default(DateOnly));
        RuleFor(x => x.SupplierId).GreaterThan(0);
        RuleFor(x => x.WarehouseId).GreaterThan(0);
        RuleFor(x => x.CurrencyId).GreaterThan(0);
        RuleFor(x => x.ExchangeRate).GreaterThan(0).When(x => x.ExchangeRate.HasValue);
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one line is required.");
        RuleFor(x => x.Lines).Must(l => l.Select(i => i.ItemId).Distinct().Count() == l.Count)
            .When(x => x.Lines is not null).WithMessage("An item may appear only once per return.");
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(i => i.ItemId).GreaterThan(0);
            l.RuleFor(i => i.Quantity).GreaterThan(0);
            l.RuleFor(i => i.UnitPrice).GreaterThanOrEqualTo(0);
        });
    }
}
