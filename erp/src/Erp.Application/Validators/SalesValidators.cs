using Erp.Application.Sales;
using FluentValidation;

namespace Erp.Application.Validators;

public class SalesOrderInputValidator : AbstractValidator<SalesOrderInput>
{
    public SalesOrderInputValidator()
    {
        RuleFor(x => x.Date).NotEqual(default(DateOnly));
        RuleFor(x => x.CustomerId).GreaterThan(0);
        RuleFor(x => x.CurrencyId).GreaterThan(0);
        RuleFor(x => x.ExchangeRate).GreaterThan(0).When(x => x.ExchangeRate.HasValue);
        RuleFor(x => x.RequiredDate).GreaterThanOrEqualTo(x => x.Date).When(x => x.RequiredDate.HasValue)
            .WithMessage("Required date cannot be before the order date.");
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

public class DeliveryNoteInputValidator : AbstractValidator<DeliveryNoteInput>
{
    public DeliveryNoteInputValidator()
    {
        RuleFor(x => x.Date).NotEqual(default(DateOnly));
        RuleFor(x => x.CustomerId).GreaterThan(0);
        RuleFor(x => x.WarehouseId).GreaterThan(0);
        RuleFor(x => x.CurrencyId).GreaterThan(0);
        RuleFor(x => x.ExchangeRate).GreaterThan(0).When(x => x.ExchangeRate.HasValue);
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one line is required.");
        RuleFor(x => x.Lines).Must(l => l.Select(i => i.ItemId).Distinct().Count() == l.Count)
            .When(x => x.Lines is not null).WithMessage("An item may appear only once per delivery note.");
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(i => i.ItemId).GreaterThan(0);
            l.RuleFor(i => i.Quantity).GreaterThan(0);
            l.RuleFor(i => i.UnitPrice).GreaterThanOrEqualTo(0);
        });
    }
}

public class SalesInvoiceInputValidator : AbstractValidator<SalesInvoiceInput>
{
    public SalesInvoiceInputValidator()
    {
        RuleFor(x => x.Date).NotEqual(default(DateOnly));
        RuleFor(x => x.DueDate).GreaterThanOrEqualTo(x => x.Date).When(x => x.DueDate.HasValue)
            .WithMessage("Due date cannot be before the invoice date.");
        RuleFor(x => x.CustomerId).GreaterThan(0);
        RuleFor(x => x.CurrencyId).GreaterThan(0);
        RuleFor(x => x.ExchangeRate).GreaterThan(0).When(x => x.ExchangeRate.HasValue);
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

public class SalesReturnInputValidator : AbstractValidator<SalesReturnInput>
{
    public SalesReturnInputValidator()
    {
        RuleFor(x => x.Date).NotEqual(default(DateOnly));
        RuleFor(x => x.CustomerId).GreaterThan(0);
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

public class CustomerReceiptInputValidator : AbstractValidator<CustomerReceiptInput>
{
    public CustomerReceiptInputValidator()
    {
        RuleFor(x => x.Date).NotEqual(default(DateOnly));
        RuleFor(x => x.CustomerId).GreaterThan(0);
        RuleFor(x => x.CurrencyId).GreaterThan(0);
        RuleFor(x => x.ExchangeRate).GreaterThan(0).When(x => x.ExchangeRate.HasValue);
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Method).IsInEnum();
        RuleFor(x => x.Reference).MaximumLength(100);
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

public class TripInputValidator : AbstractValidator<TripInput>
{
    public TripInputValidator()
    {
        RuleFor(x => x.Date).NotEqual(default(DateOnly));
        RuleFor(x => x.DriverId).GreaterThan(0);
        RuleFor(x => x.VehicleId).GreaterThan(0);
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x.DeliveryNoteIds).NotNull();
        RuleFor(x => x.DeliveryNoteIds).Must(l => l.Distinct().Count() == l.Count)
            .When(x => x.DeliveryNoteIds is not null).WithMessage("A delivery note may appear only once per trip.");
        RuleForEach(x => x.DeliveryNoteIds).GreaterThan(0);
    }
}

public class StopStatusInputValidator : AbstractValidator<StopStatusInput>
{
    public StopStatusInputValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Notes).MaximumLength(300);
    }
}
