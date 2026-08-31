using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Domain.Common;
using AadhiCrackers.Domain.Entities;
using AadhiCrackers.Domain.ValueObjects;
using AadhiCrackers.Infrastructure.Identity;
using AadhiCrackers.Infrastructure.Persistence.ValueConverters;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AadhiCrackers.Infrastructure.Persistence;

public class AadhiDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, string>, IApplicationDbContext
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerAddress> CustomerAddresses => Set<CustomerAddress>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderStatusHistory> OrderStatusHistories => Set<OrderStatusHistory>();
    public DbSet<StockItem> StockItems => Set<StockItem>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderItem> PurchaseOrderItems => Set<PurchaseOrderItem>();
    public DbSet<GoodsReceipt> GoodsReceipts => Set<GoodsReceipt>();
    public DbSet<GoodsReceiptItem> GoodsReceiptItems => Set<GoodsReceiptItem>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<Promotion> Promotions => Set<Promotion>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();

    public AadhiDbContext(DbContextOptions<AadhiDbContext> options) : base(options)
    {
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.Properties<Money>().HaveConversion<MoneyConverter>();
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Product Configuration
        builder.Entity<Product>(b =>
        {
            b.HasKey(p => p.Id);
            b.HasIndex(p => p.SKU).IsUnique();
            b.HasIndex(p => p.Slug).IsUnique();
            b.HasIndex(p => p.CategoryId);
            b.HasIndex(p => p.IsActive);
            b.HasIndex(p => p.IsFeatured);
            b.HasIndex(p => p.IsBestSeller);

            b.Property(p => p.SKU).IsRequired().HasMaxLength(50);
            b.Property(p => p.Name).IsRequired().HasMaxLength(150);
            b.Property(p => p.Slug).IsRequired().HasMaxLength(200);
            b.Property(p => p.TaxRate).HasPrecision(5, 2);
            b.Property(p => p.DiscountValue).HasPrecision(18, 2);

            b.HasOne(p => p.Category)
                .WithMany(c => c.Products)
                .HasForeignKey(p => p.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(p => p.Brand)
                .WithMany(br => br.Products)
                .HasForeignKey(p => p.BrandId)
                .OnDelete(DeleteBehavior.SetNull);

            b.HasQueryFilter(p => !p.IsDeleted);
        });

        // ProductImage Configuration
        builder.Entity<ProductImage>(b =>
        {
            b.HasKey(i => i.Id);
            b.HasOne(i => i.Product)
                .WithMany(p => p.Images)
                .HasForeignKey(i => i.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Category Configuration
        builder.Entity<Category>(b =>
        {
            b.HasKey(c => c.Id);
            b.HasIndex(c => c.Slug).IsUnique();
            b.Property(c => c.Name).IsRequired().HasMaxLength(100);
            b.Property(c => c.Slug).IsRequired().HasMaxLength(150);

            b.HasOne(c => c.ParentCategory)
                .WithMany(c => c.SubCategories)
                .HasForeignKey(c => c.ParentCategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasQueryFilter(c => !c.IsDeleted);
        });

        // Brand Configuration
        builder.Entity<Brand>(b =>
        {
            b.HasKey(br => br.Id);
            b.HasIndex(br => br.Slug).IsUnique();
            b.Property(br => br.Name).IsRequired().HasMaxLength(100);

            b.HasQueryFilter(br => !br.IsDeleted);
        });

        // Customer Configuration
        builder.Entity<Customer>(b =>
        {
            b.HasKey(c => c.Id);
            b.HasIndex(c => c.Email).IsUnique();
            b.HasIndex(c => c.Phone);
            b.HasIndex(c => c.CustomerCode).IsUnique();

            b.Property(c => c.Email).IsRequired().HasMaxLength(150);
            b.Property(c => c.FirstName).IsRequired().HasMaxLength(50);
            b.Property(c => c.LastName).IsRequired().HasMaxLength(50);

            b.HasQueryFilter(c => !c.IsDeleted);
        });

        // CustomerAddress Configuration
        builder.Entity<CustomerAddress>(b =>
        {
            b.HasKey(a => a.Id);
            b.OwnsOne(a => a.Address);

            b.HasOne(a => a.Customer)
                .WithMany(c => c.Addresses)
                .HasForeignKey(a => a.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Order Configuration
        builder.Entity<Order>(b =>
        {
            b.HasKey(o => o.Id);
            b.HasIndex(o => o.OrderNumber).IsUnique();
            b.HasIndex(o => o.CustomerId);
            b.HasIndex(o => o.OrderStatus);
            b.HasIndex(o => o.PlacedAtUtc);

            b.Property(o => o.OrderNumber).IsRequired().HasMaxLength(50);

            b.OwnsOne(o => o.ShippingAddress);
            b.OwnsOne(o => o.BillingAddress);

            b.HasOne(o => o.Customer)
                .WithMany(c => c.Orders)
                .HasForeignKey(o => o.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasQueryFilter(o => !o.IsDeleted);
        });

        // OrderItem Configuration
        builder.Entity<OrderItem>(b =>
        {
            b.HasKey(oi => oi.Id);
            b.Property(oi => oi.ProductNameSnapshot).IsRequired().HasMaxLength(150);
            b.Property(oi => oi.SKUSnapshot).IsRequired().HasMaxLength(50);

            b.HasOne(oi => oi.Order)
                .WithMany(o => o.Items)
                .HasForeignKey(oi => oi.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // StockItem Configuration
        builder.Entity<StockItem>(b =>
        {
            b.HasKey(s => s.Id);
            b.HasIndex(s => new { s.ProductId, s.WarehouseId }).IsUnique();

            b.HasOne(s => s.Product)
                .WithMany()
                .HasForeignKey(s => s.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(s => s.Warehouse)
                .WithMany(w => w.StockItems)
                .HasForeignKey(s => s.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // StockMovement Configuration
        builder.Entity<StockMovement>(b =>
        {
            b.HasKey(m => m.Id);
            b.HasIndex(m => m.ProductId);
            b.HasIndex(m => m.WarehouseId);
            b.HasIndex(m => m.CreatedAtUtc);
            b.Property(m => m.Reason).IsRequired().HasMaxLength(250);
        });

        // Supplier Configuration
        builder.Entity<Supplier>(b =>
        {
            b.HasKey(s => s.Id);
            b.HasIndex(s => s.Code).IsUnique();
            b.Property(s => s.Name).IsRequired().HasMaxLength(150);
            b.HasQueryFilter(s => !s.IsDeleted);
        });

        // PurchaseOrder Configuration
        builder.Entity<PurchaseOrder>(b =>
        {
            b.HasKey(po => po.Id);
            b.HasIndex(po => po.PoNumber).IsUnique();
            b.Property(po => po.PoNumber).IsRequired().HasMaxLength(50);

            b.HasOne(po => po.Supplier)
                .WithMany(s => s.PurchaseOrders)
                .HasForeignKey(po => po.SupplierId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasQueryFilter(po => !po.IsDeleted);
        });

        // Invoice Configuration
        builder.Entity<Invoice>(b =>
        {
            b.HasKey(i => i.Id);
            b.HasIndex(i => i.InvoiceNumber).IsUnique();
            b.HasIndex(i => i.OrderId);

            b.HasOne(i => i.Order)
                .WithMany(o => o.Invoices)
                .HasForeignKey(i => i.OrderId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasQueryFilter(i => !i.IsDeleted);
        });

        // Payment Configuration
        builder.Entity<Payment>(b =>
        {
            b.HasKey(p => p.Id);
            b.HasIndex(p => p.PaymentNumber).IsUnique();

            b.HasOne(p => p.Order)
                .WithMany(o => o.Payments)
                .HasForeignKey(p => p.OrderId)
                .OnDelete(DeleteBehavior.SetNull);

            b.HasQueryFilter(p => !p.IsDeleted);
        });

        // Expense Configuration
        builder.Entity<Expense>(b =>
        {
            b.HasKey(e => e.Id);
            b.HasIndex(e => e.ExpenseNumber).IsUnique();
            b.HasIndex(e => e.Category);
            b.HasIndex(e => e.ExpenseDateUtc);
            b.Property(e => e.Description).IsRequired().HasMaxLength(250);
            b.HasQueryFilter(e => !e.IsDeleted);
        });

        // Promotion Configuration
        builder.Entity<Promotion>(b =>
        {
            b.HasKey(p => p.Id);
            b.HasIndex(p => p.Code).IsUnique();
            b.Property(p => p.Code).IsRequired().HasMaxLength(50);
            b.Property(p => p.DiscountValue).HasPrecision(18, 2);
            b.HasQueryFilter(p => !p.IsDeleted);
        });

        // AuditLog Configuration
        builder.Entity<AuditLog>(b =>
        {
            b.HasKey(a => a.Id);
            b.HasIndex(a => a.TimestampUtc);
            b.HasIndex(a => a.UserId);
            b.HasIndex(a => a.CorrelationId);
            b.HasIndex(a => a.Action);
            b.HasIndex(a => a.Module);
            b.HasIndex(a => a.EntityType);
        });

        // OutboxMessage Configuration
        builder.Entity<OutboxMessage>(b =>
        {
            b.HasKey(o => o.Id);
            b.HasIndex(o => o.ProcessedOnUtc);
            b.HasIndex(o => o.OccurredOnUtc);
        });

        // SystemSetting Configuration
        builder.Entity<SystemSetting>(b =>
        {
            b.HasKey(s => s.Id);
            b.HasIndex(s => s.Key).IsUnique();
            b.HasIndex(s => s.Group);
            b.Property(s => s.Key).IsRequired().HasMaxLength(100);
            b.HasQueryFilter(s => !s.IsDeleted);
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<BaseEntity<Guid>>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAtUtc = DateTime.UtcNow;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAtUtc = DateTime.UtcNow;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
