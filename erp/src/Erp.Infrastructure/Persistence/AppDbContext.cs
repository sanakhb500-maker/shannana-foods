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
