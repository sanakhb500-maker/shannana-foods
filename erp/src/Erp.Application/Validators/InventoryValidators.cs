using Erp.Application.Inventory;
using FluentValidation;

namespace Erp.Application.Validators;

public class TransferInputValidator : AbstractValidator<TransferInput>
{
    public TransferInputValidator()
    {
        RuleFor(x => x.Date).NotEqual(default(DateOnly));
        RuleFor(x => x.FromWarehouseId).GreaterThan(0);
        RuleFor(x => x.ToWarehouseId).GreaterThan(0);
        RuleFor(x => x).Must(x => x.FromWarehouseId != x.ToWarehouseId)
            .WithName("ToWarehouseId").WithMessage("Source and destination warehouses must be different.");
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one line is required.");
        RuleFor(x => x.Lines).Must(l => l.Select(i => i.ItemId).Distinct().Count() == l.Count)
            .When(x => x.Lines is not null).WithMessage("An item may appear only once per document.");
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(i => i.ItemId).GreaterThan(0);
            l.RuleFor(i => i.Quantity).GreaterThan(0);
        });
    }
}

public class AdjustmentInputValidator : AbstractValidator<AdjustmentInput>
{
    public AdjustmentInputValidator()
    {
        RuleFor(x => x.Date).NotEqual(default(DateOnly));
        RuleFor(x => x.WarehouseId).GreaterThan(0);
        RuleFor(x => x.Reason).IsInEnum();
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one line is required.");
        RuleFor(x => x.Lines).Must(l => l.Select(i => i.ItemId).Distinct().Count() == l.Count)
            .When(x => x.Lines is not null).WithMessage("An item may appear only once per document.");
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(i => i.ItemId).GreaterThan(0);
            l.RuleFor(i => i.CountedQuantity).GreaterThanOrEqualTo(0);
            l.RuleFor(i => i.UnitCost).GreaterThanOrEqualTo(0).When(i => i.UnitCost.HasValue);
        });
    }
}
