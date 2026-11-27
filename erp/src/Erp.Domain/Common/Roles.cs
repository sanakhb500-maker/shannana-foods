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
    public const string ViewPurchasing = "View.Purchasing";
    public const string ManagePurchasing = "Manage.Purchasing";
    public const string ReceiveGoods = "Manage.GoodsReceipts";
    public const string ManageSupplierPayments = "Manage.SupplierPayments";
    public const string ViewSales = "View.Sales";
    public const string ManageSales = "Manage.Sales";
    public const string ManageDeliveries = "Manage.Deliveries";
    public const string ManageTrips = "Manage.Trips";
    public const string ManageCustomerReceipts = "Manage.CustomerReceipts";
    public const string ViewAccounting = "View.Accounting";
    public const string ManageAccounting = "Manage.Accounting";
    public const string ManageExpenseInvoices = "Manage.ExpenseInvoices";

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
        // Purchasing data carries cost prices, so it is NOT readable by every signed-in user.
        [ViewPurchasing] = [Roles.Admin, Roles.Accountant, Roles.Purchasing, Roles.StoreKeeper],
        [ManagePurchasing] = [Roles.Admin, Roles.Purchasing],
        [ReceiveGoods] = [Roles.Admin, Roles.Purchasing, Roles.StoreKeeper],
        [ManageSupplierPayments] = [Roles.Admin, Roles.Accountant],
        [ViewSales] = [Roles.Admin, Roles.Accountant, Roles.Sales, Roles.Dispatcher, Roles.StoreKeeper],
        [ManageSales] = [Roles.Admin, Roles.Sales],
        // Deliveries and returns move physical stock, so the store keeper can post them too.
        [ManageDeliveries] = [Roles.Admin, Roles.Sales, Roles.StoreKeeper],
        [ManageTrips] = [Roles.Admin, Roles.Dispatcher],
        [ManageCustomerReceipts] = [Roles.Admin, Roles.Accountant],
        [ViewAccounting] = [Roles.Admin, Roles.Accountant],
        [ManageAccounting] = [Roles.Admin, Roles.Accountant],
        [ManageExpenseInvoices] = [Roles.Admin, Roles.Accountant, Roles.Purchasing],
    };
}
