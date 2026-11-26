using Erp.Domain.Common;
using Erp.Domain.Entities;
using Erp.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Controllers;

public class CurrenciesController(AppDbContext db, IValidator<Currency> v, IAuthorizationService a)
    : CrudController<Currency>(db, v, a)
{
    protected override string WritePolicy => Policies.ManageCurrencies;
    protected override IQueryable<Currency> Order(IQueryable<Currency> q) => q.OrderByDescending(x => x.IsLocal).ThenBy(x => x.Code);
    protected override IQueryable<Currency> Search(IQueryable<Currency> q, string t) =>
        q.Where(x => x.Code.Contains(t) || x.NameAr.Contains(t) || x.NameEn.Contains(t));

    protected override async Task<string?> BeforeSaveAsync(Currency c, Currency? existing, CancellationToken ct)
    {
        c.Code = c.Code.ToUpperInvariant();
        // The local (base) currency is fixed: accounting depends on it never changing.
        if (existing is null && c.IsLocal && await Db.Currencies.AnyAsync(x => x.IsLocal, ct))
            return "A local currency already exists.";
        if (existing is not null && existing.IsLocal != c.IsLocal)
            return "The local-currency flag cannot be changed.";
        return null;
    }

    protected override Task<string?> BeforeDeactivateAsync(Currency c, CancellationToken ct) =>
        Task.FromResult<string?>(c.IsLocal ? "The local currency cannot be deactivated." : null);
}

public class ExchangeRatesController(AppDbContext db, IValidator<ExchangeRate> v, IAuthorizationService a)
    : CrudController<ExchangeRate>(db, v, a)
{
    protected override string WritePolicy => Policies.ManageCurrencies;
    protected override IQueryable<ExchangeRate> Order(IQueryable<ExchangeRate> q) => q.OrderByDescending(x => x.Date).ThenBy(x => x.CurrencyId);

    protected override IQueryable<ExchangeRate> Filter(IQueryable<ExchangeRate> q, IQueryCollection query) =>
        int.TryParse(query["currencyId"], out var id) ? q.Where(x => x.CurrencyId == id) : q;

    protected override async Task<string?> BeforeSaveAsync(ExchangeRate r, ExchangeRate? existing, CancellationToken ct)
    {
        var cur = await Db.Currencies.AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.CurrencyId, ct);
        if (cur is null) return "Currency not found.";
        if (cur.IsLocal) return "The local currency always has a rate of 1; add rates for foreign currencies only.";
        return null;
    }
}

public class UnitsController(AppDbContext db, IValidator<Unit> v, IAuthorizationService a) : CrudController<Unit>(db, v, a)
{
    protected override string WritePolicy => Policies.ManageItems;
    protected override IQueryable<Unit> Order(IQueryable<Unit> q) => q.OrderBy(x => x.Code);
    protected override IQueryable<Unit> Search(IQueryable<Unit> q, string t) =>
        q.Where(x => x.Code.Contains(t) || x.NameAr.Contains(t) || x.NameEn.Contains(t));
    protected override Task<string?> BeforeSaveAsync(Unit u, Unit? e, CancellationToken ct) { u.Code = u.Code.ToUpperInvariant(); return Task.FromResult<string?>(null); }
}

public class ItemCategoriesController(AppDbContext db, IValidator<ItemCategory> v, IAuthorizationService a)
    : CrudController<ItemCategory>(db, v, a)
{
    protected override string WritePolicy => Policies.ManageItems;
    protected override IQueryable<ItemCategory> Order(IQueryable<ItemCategory> q) => q.OrderBy(x => x.NameEn);
    protected override IQueryable<ItemCategory> Search(IQueryable<ItemCategory> q, string t) =>
        q.Where(x => x.NameAr.Contains(t) || x.NameEn.Contains(t));
}

public class ItemsController(AppDbContext db, IValidator<Item> v, IAuthorizationService a) : CrudController<Item>(db, v, a)
{
    protected override string WritePolicy => Policies.ManageItems;
    protected override IQueryable<Item> Order(IQueryable<Item> q) => q.OrderBy(x => x.Code);
    protected override IQueryable<Item> Search(IQueryable<Item> q, string t) =>
        q.Where(x => x.Code.Contains(t) || x.NameAr.Contains(t) || x.NameEn.Contains(t) || (x.Barcode != null && x.Barcode.Contains(t)));
    protected override IQueryable<Item> Filter(IQueryable<Item> q, IQueryCollection query) =>
        int.TryParse(query["categoryId"], out var id) ? q.Where(x => x.CategoryId == id) : q;

    protected override async Task<string?> BeforeSaveAsync(Item i, Item? e, CancellationToken ct)
    {
        i.Code = i.Code.ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(i.Barcode)) i.Barcode = null;
        if (!await Db.Units.AnyAsync(x => x.Id == i.UnitId && x.IsActive, ct)) return "Unit not found or inactive.";
        if (i.CategoryId is not null && !await Db.ItemCategories.AnyAsync(x => x.Id == i.CategoryId && x.IsActive, ct))
            return "Category not found or inactive.";
        return null;
    }
}

public class WarehousesController(AppDbContext db, IValidator<Warehouse> v, IAuthorizationService a)
    : CrudController<Warehouse>(db, v, a)
{
    protected override string WritePolicy => Policies.ManageWarehouses;
    protected override IQueryable<Warehouse> Order(IQueryable<Warehouse> q) => q.OrderBy(x => x.Code);
    protected override IQueryable<Warehouse> Search(IQueryable<Warehouse> q, string t) =>
        q.Where(x => x.Code.Contains(t) || x.NameAr.Contains(t) || x.NameEn.Contains(t));
    protected override Task<string?> BeforeSaveAsync(Warehouse w, Warehouse? e, CancellationToken ct) { w.Code = w.Code.ToUpperInvariant(); return Task.FromResult<string?>(null); }
}

public class SuppliersController(AppDbContext db, IValidator<Supplier> v, IAuthorizationService a)
    : CrudController<Supplier>(db, v, a)
{
    protected override string WritePolicy => Policies.ManageSuppliers;
    protected override IQueryable<Supplier> Order(IQueryable<Supplier> q) => q.OrderBy(x => x.Code);
    protected override IQueryable<Supplier> Search(IQueryable<Supplier> q, string t) =>
        q.Where(x => x.Code.Contains(t) || x.NameAr.Contains(t) || x.NameEn.Contains(t) || (x.Phone != null && x.Phone.Contains(t)));

    protected override async Task<string?> BeforeSaveAsync(Supplier s, Supplier? e, CancellationToken ct)
    {
        s.Code = s.Code.ToUpperInvariant();
        if (s.CurrencyId is not null && !await Db.Currencies.AnyAsync(x => x.Id == s.CurrencyId && x.IsActive, ct)) return "Currency not found or inactive.";
        return null;
    }
}

public class CustomersController(AppDbContext db, IValidator<Customer> v, IAuthorizationService a)
    : CrudController<Customer>(db, v, a)
{
    protected override string WritePolicy => Policies.ManageCustomers;
    protected override IQueryable<Customer> Order(IQueryable<Customer> q) => q.OrderBy(x => x.Code);
    protected override IQueryable<Customer> Search(IQueryable<Customer> q, string t) =>
        q.Where(x => x.Code.Contains(t) || x.NameAr.Contains(t) || x.NameEn.Contains(t) || (x.Phone != null && x.Phone.Contains(t)));

    protected override async Task<string?> BeforeSaveAsync(Customer c, Customer? e, CancellationToken ct)
    {
        c.Code = c.Code.ToUpperInvariant();
        if (c.CurrencyId is not null && !await Db.Currencies.AnyAsync(x => x.Id == c.CurrencyId && x.IsActive, ct)) return "Currency not found or inactive.";
        return null;
    }
}

public class DriversController(AppDbContext db, IValidator<Driver> v, IAuthorizationService a) : CrudController<Driver>(db, v, a)
{
    protected override string WritePolicy => Policies.ManageFleet;
    protected override IQueryable<Driver> Order(IQueryable<Driver> q) => q.OrderBy(x => x.Name);
    protected override IQueryable<Driver> Search(IQueryable<Driver> q, string t) =>
        q.Where(x => x.Name.Contains(t) || (x.Phone != null && x.Phone.Contains(t)));
}

public class VehiclesController(AppDbContext db, IValidator<Vehicle> v, IAuthorizationService a) : CrudController<Vehicle>(db, v, a)
{
    protected override string WritePolicy => Policies.ManageFleet;
    protected override IQueryable<Vehicle> Order(IQueryable<Vehicle> q) => q.OrderBy(x => x.PlateNumber);
    protected override IQueryable<Vehicle> Search(IQueryable<Vehicle> q, string t) =>
        q.Where(x => x.PlateNumber.Contains(t) || (x.Description != null && x.Description.Contains(t)));
}
