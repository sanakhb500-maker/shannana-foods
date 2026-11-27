using Erp.Application.Common;
using Erp.Domain.Common;
using Erp.Domain.Entities;
using Erp.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Erp.Infrastructure.Persistence;

public class AppDbContext : IdentityDbContext<AppUser, AppRole, int>
{
    private readonly ICurrentUser? _currentUser;

    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser? currentUser = null) : base(options)
        => _currentUser = currentUser;

    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();
    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<ItemCategory> ItemCategories => Set<ItemCategory>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Driver> Drivers => Set<Driver>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();

    // inventory
    public DbSet<StockBalance> StockBalances => Set<StockBalance>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<StockTransfer> StockTransfers => Set<StockTransfer>();
    public DbSet<StockTransferLine> StockTransferLines => Set<StockTransferLine>();
    public DbSet<StockAdjustment> StockAdjustments => Set<StockAdjustment>();
    public DbSet<StockAdjustmentLine> StockAdjustmentLines => Set<StockAdjustmentLine>();

    // purchasing
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
    public DbSet<GoodsReceipt> GoodsReceipts => Set<GoodsReceipt>();
    public DbSet<GoodsReceiptLine> GoodsReceiptLines => Set<GoodsReceiptLine>();
    public DbSet<PurchaseInvoice> PurchaseInvoices => Set<PurchaseInvoice>();
    public DbSet<PurchaseInvoiceLine> PurchaseInvoiceLines => Set<PurchaseInvoiceLine>();
    public DbSet<SupplierPayment> SupplierPayments => Set<SupplierPayment>();

    // sales and distribution
    public DbSet<SalesOrder> SalesOrders => Set<SalesOrder>();
    public DbSet<SalesOrderLine> SalesOrderLines => Set<SalesOrderLine>();
    public DbSet<DeliveryNote> DeliveryNotes => Set<DeliveryNote>();
    public DbSet<DeliveryNoteLine> DeliveryNoteLines => Set<DeliveryNoteLine>();
    public DbSet<SalesInvoice> SalesInvoices => Set<SalesInvoice>();
    public DbSet<SalesInvoiceLine> SalesInvoiceLines => Set<SalesInvoiceLine>();
    public DbSet<SalesReturn> SalesReturns => Set<SalesReturn>();
    public DbSet<SalesReturnLine> SalesReturnLines => Set<SalesReturnLine>();
    public DbSet<CustomerReceipt> CustomerReceipts => Set<CustomerReceipt>();
    public DbSet<DistributionTrip> DistributionTrips => Set<DistributionTrip>();
    public DbSet<TripStop> TripStops => Set<TripStop>();

    // accounting
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<JournalLine> JournalLines => Set<JournalLine>();
    public DbSet<AccountingSetting> AccountingSettings => Set<AccountingSetting>();
    public DbSet<DocumentSequence> DocumentSequences => Set<DocumentSequence>();

    // expenses, settlement, revaluation
    public DbSet<ExpenseInvoice> ExpenseInvoices => Set<ExpenseInvoice>();
    public DbSet<ExpenseInvoiceLine> ExpenseInvoiceLines => Set<ExpenseInvoiceLine>();
    public DbSet<ExpenseVoucher> ExpenseVouchers => Set<ExpenseVoucher>();
    public DbSet<ExpenseVoucherLine> ExpenseVoucherLines => Set<ExpenseVoucherLine>();
    public DbSet<SupplierPaymentAllocation> SupplierPaymentAllocations => Set<SupplierPaymentAllocation>();
    public DbSet<CustomerReceiptAllocation> CustomerReceiptAllocations => Set<CustomerReceiptAllocation>();
    public DbSet<RevaluationRun> RevaluationRuns => Set<RevaluationRun>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // Money / rates / quantities: fixed precision, never float.
        foreach (var p in b.Model.GetEntityTypes().SelectMany(e => e.GetProperties())
                     .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
        {
            p.SetPrecision(18);
            p.SetScale(4);
        }

        b.Entity<Currency>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => x.IsLocal).IsUnique().HasFilter("[IsLocal] = 1"); // only one local currency
            e.Property(x => x.Code).HasMaxLength(3);
            e.Property(x => x.NameAr).HasMaxLength(100);
            e.Property(x => x.NameEn).HasMaxLength(100);
            e.Property(x => x.Symbol).HasMaxLength(8);
        });

        b.Entity<ExchangeRate>(e =>
        {
            e.HasIndex(x => new { x.CurrencyId, x.Date }).IsUnique();
            e.HasOne<Currency>().WithMany().HasForeignKey(x => x.CurrencyId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_ExchangeRate_Rate", "[Rate] > 0"));
        });

        b.Entity<Unit>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(30);
            e.Property(x => x.NameAr).HasMaxLength(200);
            e.Property(x => x.NameEn).HasMaxLength(200);
        });

        b.Entity<ItemCategory>(e =>
        {
            e.Property(x => x.NameAr).HasMaxLength(200);
            e.Property(x => x.NameEn).HasMaxLength(200);
        });

        b.Entity<Item>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => x.Barcode).IsUnique().HasFilter("[Barcode] IS NOT NULL");
            e.Property(x => x.Code).HasMaxLength(30);
            e.Property(x => x.NameAr).HasMaxLength(200);
            e.Property(x => x.NameEn).HasMaxLength(200);
            e.Property(x => x.Barcode).HasMaxLength(50);
            e.HasOne<Unit>().WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ItemCategory>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Warehouse>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(30);
            e.Property(x => x.NameAr).HasMaxLength(200);
            e.Property(x => x.NameEn).HasMaxLength(200);
            e.Property(x => x.Address).HasMaxLength(300);
        });

        b.Entity<Supplier>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(30);
            e.Property(x => x.NameAr).HasMaxLength(200);
            e.Property(x => x.NameEn).HasMaxLength(200);
            e.Property(x => x.Phone).HasMaxLength(30);
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.Address).HasMaxLength(300);
            e.Property(x => x.TaxNumber).HasMaxLength(50);
            e.HasOne<Currency>().WithMany().HasForeignKey(x => x.CurrencyId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Customer>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(30);
            e.Property(x => x.NameAr).HasMaxLength(200);
            e.Property(x => x.NameEn).HasMaxLength(200);
            e.Property(x => x.Phone).HasMaxLength(30);
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.Address).HasMaxLength(300);
            e.Property(x => x.TaxNumber).HasMaxLength(50);
            e.Property(x => x.Type).HasConversion<int>();
            e.HasOne<Currency>().WithMany().HasForeignKey(x => x.CurrencyId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Driver>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Phone).HasMaxLength(30);
            e.Property(x => x.LicenseNumber).HasMaxLength(50);
        });

        b.Entity<Vehicle>(e =>
        {
            e.HasIndex(x => x.PlateNumber).IsUnique();
            e.Property(x => x.PlateNumber).HasMaxLength(20);
            e.Property(x => x.Description).HasMaxLength(200);
        });

        ConfigureInventory(b);
        ConfigurePurchasing(b);
        ConfigureSales(b);
        ConfigureAccounting(b);
        ConfigureExpensesAndSettlement(b);

        b.Entity<DocumentSequence>(e =>
        {
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(10);
            e.Property(x => x.RowVersion).IsRowVersion();
        });
    }

    private static void ConfigureExpensesAndSettlement(ModelBuilder b)
    {
        b.Entity<ExpenseInvoice>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique().HasFilter("[Number] IS NOT NULL");
            e.HasIndex(x => new { x.SupplierId, x.Date });
            e.Property(x => x.Number).HasMaxLength(30);
            e.Property(x => x.SupplierInvoiceNumber).HasMaxLength(50);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.Status).HasConversion<int>();
            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Currency>().WithMany().HasForeignKey(x => x.CurrencyId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.ExpenseInvoiceId).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t => t.HasCheckConstraint("CK_ExpenseInvoice_Rate", "[ExchangeRate] > 0"));
        });
        b.Entity<ExpenseInvoiceLine>(e =>
        {
            e.Property(x => x.Description).HasMaxLength(200);
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_ExpenseInvoiceLine_Amount", "[Amount] > 0"));
        });

        b.Entity<ExpenseVoucher>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique().HasFilter("[Number] IS NOT NULL");
            e.Property(x => x.Number).HasMaxLength(30);
            e.Property(x => x.Payee).HasMaxLength(200);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.Status).HasConversion<int>();
            e.Property(x => x.Method).HasConversion<int>();
            e.HasOne<Currency>().WithMany().HasForeignKey(x => x.CurrencyId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.ExpenseVoucherId).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t => t.HasCheckConstraint("CK_ExpenseVoucher_Rate", "[ExchangeRate] > 0"));
        });
        b.Entity<ExpenseVoucherLine>(e =>
        {
            e.Property(x => x.Description).HasMaxLength(200);
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_ExpenseVoucherLine_Amount", "[Amount] > 0"));
        });

        b.Entity<SupplierPayment>().HasMany(x => x.Allocations).WithOne().HasForeignKey(a => a.SupplierPaymentId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<SupplierPaymentAllocation>(e =>
        {
            e.HasIndex(x => x.PurchaseInvoiceId);
            e.HasIndex(x => x.ExpenseInvoiceId);
            e.HasOne<PurchaseInvoice>().WithMany().HasForeignKey(x => x.PurchaseInvoiceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ExpenseInvoice>().WithMany().HasForeignKey(x => x.ExpenseInvoiceId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_SupplierPaymentAllocation_One",
                "[Amount] > 0 AND (([PurchaseInvoiceId] IS NOT NULL AND [ExpenseInvoiceId] IS NULL) OR ([PurchaseInvoiceId] IS NULL AND [ExpenseInvoiceId] IS NOT NULL))"));
        });

        b.Entity<CustomerReceipt>().HasMany(x => x.Allocations).WithOne().HasForeignKey(a => a.CustomerReceiptId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<CustomerReceiptAllocation>(e =>
        {
            e.HasIndex(x => x.SalesInvoiceId);
            e.HasOne<SalesInvoice>().WithMany().HasForeignKey(x => x.SalesInvoiceId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_CustomerReceiptAllocation_Amount", "[Amount] > 0"));
        });

        b.Entity<RevaluationRun>(e =>
        {
            e.HasIndex(x => x.AsOf).IsUnique();
            e.HasOne(x => x.JournalEntry).WithMany().HasForeignKey(x => x.JournalEntryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ReversalEntry).WithMany().HasForeignKey(x => x.ReversalEntryId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureAccounting(ModelBuilder b)
    {
        b.Entity<Account>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => x.SystemRole).IsUnique().HasFilter("[SystemRole] IS NOT NULL"); // one account per posting role
            e.Property(x => x.Code).HasMaxLength(20);
            e.Property(x => x.NameAr).HasMaxLength(200);
            e.Property(x => x.NameEn).HasMaxLength(200);
            e.Property(x => x.SystemRole).HasMaxLength(30);
            e.Property(x => x.Type).HasConversion<int>();
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<JournalEntry>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique().HasFilter("[Number] IS NOT NULL");
            e.Property(x => x.Number).HasMaxLength(30);
            e.HasIndex(x => x.Date);
            e.HasIndex(x => new { x.SourceType, x.SourceId });
            e.Property(x => x.Description).HasMaxLength(300);
            e.Property(x => x.SourceType).HasMaxLength(30);
            e.Property(x => x.PostedBy).HasMaxLength(256);
            e.Property(x => x.Status).HasConversion<int>();
            e.HasOne<JournalEntry>().WithMany().HasForeignKey(x => x.ReversalOfId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.JournalEntryId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<JournalLine>(e =>
        {
            e.HasIndex(x => x.AccountId);
            e.HasIndex(x => new { x.PartyType, x.PartyId });
            e.Property(x => x.Description).HasMaxLength(200);
            e.Property(x => x.PartyType).HasConversion<int>();
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            // exactly one side per line, never negative
            e.ToTable(t => t.HasCheckConstraint("CK_JournalLine_Side", "[Debit] >= 0 AND [Credit] >= 0 AND (([Debit] > 0 AND [Credit] = 0) OR ([Debit] = 0 AND [Credit] > 0))"));
        });

        b.Entity<AccountingSetting>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.UpdatedBy).HasMaxLength(256);
            e.ToTable(t => t.HasCheckConstraint("CK_AccountingSetting_Single", "[Id] = 1"));
        });
    }

    private static void ConfigureSales(ModelBuilder b)
    {
        b.Entity<SalesOrder>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique().HasFilter("[Number] IS NOT NULL");
            e.HasIndex(x => new { x.CustomerId, x.Date });
            e.Property(x => x.Number).HasMaxLength(30);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.Status).HasConversion<int>();
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Currency>().WithMany().HasForeignKey(x => x.CurrencyId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.SalesOrderId).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t => t.HasCheckConstraint("CK_SalesOrder_Rate", "[ExchangeRate] > 0"));
        });

        b.Entity<SalesOrderLine>(e =>
        {
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_SalesOrderLine_Qty", "[Quantity] > 0 AND [UnitPrice] >= 0");
                t.HasCheckConstraint("CK_SalesOrderLine_Progress", "[DeliveredQuantity] >= 0 AND [InvoicedQuantity] >= 0");
            });
        });

        b.Entity<DeliveryNote>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique().HasFilter("[Number] IS NOT NULL");
            e.Property(x => x.Number).HasMaxLength(30);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.Status).HasConversion<int>();
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Currency>().WithMany().HasForeignKey(x => x.CurrencyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SalesOrder>().WithMany().HasForeignKey(x => x.SalesOrderId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.DeliveryNoteId).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t => t.HasCheckConstraint("CK_DeliveryNote_Rate", "[ExchangeRate] > 0"));
        });

        b.Entity<DeliveryNoteLine>(e =>
        {
            e.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SalesOrderLine>().WithMany().HasForeignKey(x => x.SalesOrderLineId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_DeliveryNoteLine_Qty", "[Quantity] > 0 AND [UnitPrice] >= 0"));
        });

        b.Entity<SalesInvoice>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique().HasFilter("[Number] IS NOT NULL");
            e.HasIndex(x => new { x.CustomerId, x.Date });
            e.Property(x => x.Number).HasMaxLength(30);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.Status).HasConversion<int>();
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Currency>().WithMany().HasForeignKey(x => x.CurrencyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SalesOrder>().WithMany().HasForeignKey(x => x.SalesOrderId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.SalesInvoiceId).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t => t.HasCheckConstraint("CK_SalesInvoice_Rate", "[ExchangeRate] > 0"));
        });

        b.Entity<SalesInvoiceLine>(e =>
        {
            e.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SalesOrderLine>().WithMany().HasForeignKey(x => x.SalesOrderLineId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_SalesInvoiceLine_Qty", "[Quantity] > 0 AND [UnitPrice] >= 0"));
        });

        b.Entity<SalesReturn>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique().HasFilter("[Number] IS NOT NULL");
            e.HasIndex(x => new { x.CustomerId, x.Date });
            e.Property(x => x.Number).HasMaxLength(30);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.Status).HasConversion<int>();
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Currency>().WithMany().HasForeignKey(x => x.CurrencyId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.SalesReturnId).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t => t.HasCheckConstraint("CK_SalesReturn_Rate", "[ExchangeRate] > 0"));
        });

        b.Entity<SalesReturnLine>(e =>
        {
            e.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_SalesReturnLine_Qty", "[Quantity] > 0 AND [UnitPrice] >= 0"));
        });

        b.Entity<CustomerReceipt>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique().HasFilter("[Number] IS NOT NULL");
            e.HasIndex(x => new { x.CustomerId, x.Date });
            e.Property(x => x.Number).HasMaxLength(30);
            e.Property(x => x.Reference).HasMaxLength(100);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.Status).HasConversion<int>();
            e.Property(x => x.Method).HasConversion<int>();
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Currency>().WithMany().HasForeignKey(x => x.CurrencyId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_CustomerReceipt_Amount", "[Amount] > 0 AND [ExchangeRate] > 0"));
        });

        b.Entity<DistributionTrip>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique().HasFilter("[Number] IS NOT NULL");
            e.HasIndex(x => new { x.Date, x.DriverId });
            e.HasIndex(x => new { x.Date, x.VehicleId });
            e.Property(x => x.Number).HasMaxLength(30);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.Status).HasConversion<int>();
            e.HasOne<Driver>().WithMany().HasForeignKey(x => x.DriverId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Vehicle>().WithMany().HasForeignKey(x => x.VehicleId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Stops).WithOne().HasForeignKey(s => s.TripId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<TripStop>(e =>
        {
            e.HasIndex(x => x.DeliveryNoteId);
            e.Property(x => x.Notes).HasMaxLength(300);
            e.Property(x => x.Status).HasConversion<int>();
            e.HasOne<DeliveryNote>().WithMany().HasForeignKey(x => x.DeliveryNoteId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigurePurchasing(ModelBuilder b)
    {
        b.Entity<PurchaseOrder>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique().HasFilter("[Number] IS NOT NULL");
            e.HasIndex(x => new { x.SupplierId, x.Date });
            e.Property(x => x.Number).HasMaxLength(30);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.Status).HasConversion<int>();
            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Currency>().WithMany().HasForeignKey(x => x.CurrencyId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.PurchaseOrderId).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t => t.HasCheckConstraint("CK_PurchaseOrder_Rate", "[ExchangeRate] > 0"));
        });

        b.Entity<PurchaseOrderLine>(e =>
        {
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_PurchaseOrderLine_Qty", "[Quantity] > 0 AND [UnitPrice] >= 0");
                t.HasCheckConstraint("CK_PurchaseOrderLine_Progress", "[ReceivedQuantity] >= 0 AND [InvoicedQuantity] >= 0");
            });
        });

        b.Entity<GoodsReceipt>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique().HasFilter("[Number] IS NOT NULL");
            e.Property(x => x.Number).HasMaxLength(30);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.Status).HasConversion<int>();
            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Currency>().WithMany().HasForeignKey(x => x.CurrencyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PurchaseOrder>().WithMany().HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.GoodsReceiptId).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t => t.HasCheckConstraint("CK_GoodsReceipt_Rate", "[ExchangeRate] > 0"));
        });

        b.Entity<GoodsReceiptLine>(e =>
        {
            e.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PurchaseOrderLine>().WithMany().HasForeignKey(x => x.PurchaseOrderLineId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_GoodsReceiptLine_Qty", "[Quantity] > 0 AND [UnitPrice] >= 0"));
        });

        b.Entity<PurchaseInvoice>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique().HasFilter("[Number] IS NOT NULL");
            e.HasIndex(x => new { x.SupplierId, x.Date });
            e.Property(x => x.Number).HasMaxLength(30);
            e.Property(x => x.SupplierInvoiceNumber).HasMaxLength(50);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.Status).HasConversion<int>();
            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Currency>().WithMany().HasForeignKey(x => x.CurrencyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PurchaseOrder>().WithMany().HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.PurchaseInvoiceId).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t => t.HasCheckConstraint("CK_PurchaseInvoice_Rate", "[ExchangeRate] > 0"));
        });

        b.Entity<PurchaseInvoiceLine>(e =>
        {
            e.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PurchaseOrderLine>().WithMany().HasForeignKey(x => x.PurchaseOrderLineId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_PurchaseInvoiceLine_Qty", "[Quantity] > 0 AND [UnitPrice] >= 0"));
        });

        b.Entity<SupplierPayment>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique().HasFilter("[Number] IS NOT NULL");
            e.HasIndex(x => new { x.SupplierId, x.Date });
            e.Property(x => x.Number).HasMaxLength(30);
            e.Property(x => x.Reference).HasMaxLength(100);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.Status).HasConversion<int>();
            e.Property(x => x.Method).HasConversion<int>();
            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Currency>().WithMany().HasForeignKey(x => x.CurrencyId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_SupplierPayment_Amount", "[Amount] > 0 AND [ExchangeRate] > 0"));
        });
    }

    private static void ConfigureInventory(ModelBuilder b)
    {
        b.Entity<StockBalance>(e =>
        {
            e.HasIndex(x => new { x.ItemId, x.WarehouseId }).IsUnique();
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<StockMovement>(e =>
        {
            e.HasIndex(x => new { x.ItemId, x.WarehouseId, x.Date });
            e.HasIndex(x => new { x.DocumentType, x.DocumentId });
            e.Property(x => x.DocumentType).HasMaxLength(30);
            e.Property(x => x.DocumentNumber).HasMaxLength(30);
            e.Property(x => x.CreatedBy).HasMaxLength(256);
            e.Property(x => x.Type).HasConversion<int>();
            e.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<StockMovement>().WithMany().HasForeignKey(x => x.ReversalOfId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<StockTransfer>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique().HasFilter("[Number] IS NOT NULL");
            e.Property(x => x.Number).HasMaxLength(30);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.Status).HasConversion<int>();
            e.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.FromWarehouseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.ToWarehouseId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.StockTransferId).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t => t.HasCheckConstraint("CK_StockTransfer_Warehouses", "[FromWarehouseId] <> [ToWarehouseId]"));
        });

        b.Entity<StockTransferLine>(e =>
        {
            e.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_StockTransferLine_Qty", "[Quantity] > 0"));
        });

        b.Entity<StockAdjustment>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique().HasFilter("[Number] IS NOT NULL");
            e.Property(x => x.Number).HasMaxLength(30);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.Status).HasConversion<int>();
            e.Property(x => x.Reason).HasConversion<int>();
            e.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.StockAdjustmentId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<StockAdjustmentLine>(e =>
        {
            e.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_StockAdjustmentLine_Counted", "[CountedQuantity] >= 0"));
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var who = _currentUser?.UserName;
        foreach (var e in ChangeTracker.Entries<AuditableEntity>())
        {
            if (e.State == EntityState.Added)
            {
                e.Entity.CreatedAt = now;
                e.Entity.CreatedBy = who;
            }
            else if (e.State == EntityState.Modified)
            {
                e.Property(x => x.CreatedAt).IsModified = false;
                e.Property(x => x.CreatedBy).IsModified = false;
                e.Entity.UpdatedAt = now;
                e.Entity.UpdatedBy = who;
            }
        }
        return base.SaveChangesAsync(ct);
    }
}
