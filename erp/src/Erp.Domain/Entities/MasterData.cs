using Erp.Domain.Common;

namespace Erp.Domain.Entities;

// NOTE: entities carry scalar properties and foreign-key ids only (no navigation
// properties) so they can be used directly as API contracts.

public class Currency : AuditableEntity
{
    public string Code { get; set; } = "";      // ISO 4217, e.g. USD
    public string NameAr { get; set; } = "";
    public string NameEn { get; set; } = "";
    public string Symbol { get; set; } = "";
    /// <summary>Exactly one currency is the local/base currency.</summary>
    public bool IsLocal { get; set; }
}

/// <summary>1 unit of <see cref="CurrencyId"/> = <see cref="Rate"/> units of the local currency.</summary>
public class ExchangeRate : AuditableEntity
{
    public int CurrencyId { get; set; }
    public DateOnly Date { get; set; }
    public decimal Rate { get; set; }
}

public class Unit : AuditableEntity
{
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string NameEn { get; set; } = "";
}

public class ItemCategory : AuditableEntity
{
    public string NameAr { get; set; } = "";
    public string NameEn { get; set; } = "";
}

public class Item : AuditableEntity
{
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string NameEn { get; set; } = "";
    public int UnitId { get; set; }
    public int? CategoryId { get; set; }
    public string? Barcode { get; set; }
    /// <summary>Standard/initial cost in local currency (moving-average cost is computed in the inventory phase).</summary>
    public decimal CostPrice { get; set; }
    /// <summary>Retail price list (local currency).</summary>
    public decimal SalePrice { get; set; }
    /// <summary>Distributor price list (local currency). Empty = distributors pay the retail price.</summary>
    public decimal? DistributorPrice { get; set; }
    /// <summary>Weight of one unit in kg, used to check a trip against the vehicle's capacity (0 = unknown).</summary>
    public decimal WeightKg { get; set; }
    public decimal ReorderLevel { get; set; }
}

public class Warehouse : AuditableEntity
{
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string NameEn { get; set; } = "";
    public string? Address { get; set; }
}

public class Supplier : AuditableEntity
{
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string NameEn { get; set; } = "";
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? TaxNumber { get; set; }
    public int? CurrencyId { get; set; }
}

public enum CustomerType { Retail = 1, Distributor = 2 }

public class Customer : AuditableEntity
{
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string NameEn { get; set; } = "";
    public CustomerType Type { get; set; } = CustomerType.Retail;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? TaxNumber { get; set; }
    public int? CurrencyId { get; set; }
    public decimal CreditLimit { get; set; }
}

public class Driver : AuditableEntity
{
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public string? LicenseNumber { get; set; }
}

public class Vehicle : AuditableEntity
{
    public string PlateNumber { get; set; } = "";
    public string? Description { get; set; }
    public decimal CapacityKg { get; set; }
}
