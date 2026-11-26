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
