namespace Erp.Domain.Common;

public static class Roles
{
    public const string Admin = "Admin";
    public const string Accountant = "Accountant";
    public const string StoreKeeper = "StoreKeeper";
    public const string Purchasing = "Purchasing";
    public const string Sales = "Sales";
    public const string Dispatcher = "Dispatcher";

    public static readonly string[] All =
        [Admin, Accountant, StoreKeeper, Purchasing, Sales, Dispatcher];
}

/// <summary>Authorization policy names. Each maps to the roles allowed to WRITE that area (everyone signed-in can read).</summary>
public static class Policies
{
    public const string ManageUsers = "Manage.Users";
    public const string ManageCurrencies = "Manage.Currencies";
    public const string ManageItems = "Manage.Items";
    public const string ManageWarehouses = "Manage.Warehouses";
    public const string ManageSuppliers = "Manage.Suppliers";
    public const string ManageCustomers = "Manage.Customers";
    public const string ManageFleet = "Manage.Fleet";
    public const string ManageInventory = "Manage.Inventory";

    public static readonly IReadOnlyDictionary<string, string[]> Map = new Dictionary<string, string[]>
    {
        [ManageUsers] = [Roles.Admin],
        [ManageCurrencies] = [Roles.Admin, Roles.Accountant],
        [ManageItems] = [Roles.Admin, Roles.StoreKeeper, Roles.Purchasing],
        [ManageWarehouses] = [Roles.Admin, Roles.StoreKeeper],
        [ManageSuppliers] = [Roles.Admin, Roles.Purchasing, Roles.Accountant],
        [ManageCustomers] = [Roles.Admin, Roles.Sales, Roles.Accountant],
        [ManageFleet] = [Roles.Admin, Roles.Dispatcher],
        [ManageInventory] = [Roles.Admin, Roles.StoreKeeper],
    };
}
