using Erp.Domain.Entities;
using FluentValidation;

namespace Erp.Application.Validators;

internal static class Rules
{
    public static IRuleBuilderOptions<T, string> Code<T>(this IRuleBuilder<T, string> r) =>
        r.NotEmpty().MaximumLength(30).Matches("^[A-Za-z0-9._-]+$").WithMessage("Code may contain letters, digits, '.', '_' and '-' only.");
    public static IRuleBuilderOptions<T, string> Name<T>(this IRuleBuilder<T, string> r) =>
        r.NotEmpty().MaximumLength(200);
}

public class CurrencyValidator : AbstractValidator<Currency>
{
    public CurrencyValidator()
    {
        RuleFor(x => x.Code).NotEmpty().Length(3).Matches("^[A-Za-z]{3}$");
        RuleFor(x => x.NameAr).Name();
        RuleFor(x => x.NameEn).Name();
        RuleFor(x => x.Symbol).NotEmpty().MaximumLength(8);
    }
}

public class ExchangeRateValidator : AbstractValidator<ExchangeRate>
{
    public ExchangeRateValidator()
    {
        RuleFor(x => x.CurrencyId).GreaterThan(0);
        RuleFor(x => x.Rate).GreaterThan(0);
        RuleFor(x => x.Date).NotEqual(default(DateOnly));
    }
}

public class UnitValidator : AbstractValidator<Unit>
{
    public UnitValidator() { RuleFor(x => x.Code).Code(); RuleFor(x => x.NameAr).Name(); RuleFor(x => x.NameEn).Name(); }
}

public class ItemCategoryValidator : AbstractValidator<ItemCategory>
{
    public ItemCategoryValidator() { RuleFor(x => x.NameAr).Name(); RuleFor(x => x.NameEn).Name(); }
}

public class ItemValidator : AbstractValidator<Item>
{
    public ItemValidator()
    {
        RuleFor(x => x.Code).Code();
        RuleFor(x => x.NameAr).Name();
        RuleFor(x => x.NameEn).Name();
        RuleFor(x => x.UnitId).GreaterThan(0);
        RuleFor(x => x.CostPrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.SalePrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.DistributorPrice).GreaterThanOrEqualTo(0).When(x => x.DistributorPrice.HasValue);
        RuleFor(x => x.WeightKg).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ReorderLevel).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Barcode).MaximumLength(50);
    }
}

public class WarehouseValidator : AbstractValidator<Warehouse>
{
    public WarehouseValidator() { RuleFor(x => x.Code).Code(); RuleFor(x => x.NameAr).Name(); RuleFor(x => x.NameEn).Name(); RuleFor(x => x.Address).MaximumLength(300); }
}

public class SupplierValidator : AbstractValidator<Supplier>
{
    public SupplierValidator()
    {
        RuleFor(x => x.Code).Code();
        RuleFor(x => x.NameAr).Name();
        RuleFor(x => x.NameEn).Name();
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Phone).MaximumLength(30);
    }
}

public class CustomerValidator : AbstractValidator<Customer>
{
    public CustomerValidator()
    {
        RuleFor(x => x.Code).Code();
        RuleFor(x => x.NameAr).Name();
        RuleFor(x => x.NameEn).Name();
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Phone).MaximumLength(30);
        RuleFor(x => x.CreditLimit).GreaterThanOrEqualTo(0);
    }
}

public class DriverValidator : AbstractValidator<Driver>
{
    public DriverValidator() { RuleFor(x => x.Name).Name(); RuleFor(x => x.Phone).MaximumLength(30); }
}

public class VehicleValidator : AbstractValidator<Vehicle>
{
    public VehicleValidator() { RuleFor(x => x.PlateNumber).NotEmpty().MaximumLength(20); RuleFor(x => x.CapacityKg).GreaterThanOrEqualTo(0); }
}
