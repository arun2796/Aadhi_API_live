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
    public DbSet<LoginHistory> LoginHistories => Set<LoginHistory>();
    public DbSet<RateLimitLog> RateLimitLogs => Set<RateLimitLog>();
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<GiftBoxItem> GiftBoxItems => Set<GiftBoxItem>();
    public DbSet<ProductReview> ProductReviews => Set<ProductReview>();
    public DbSet<Refund> Refunds => Set<Refund>();
    public DbSet<SupplierBill> SupplierBills => Set<SupplierBill>();
    public DbSet<ReturnOrder> ReturnOrders => Set<ReturnOrder>();
    public DbSet<ReturnOrderItem> ReturnOrderItems => Set<ReturnOrderItem>();
    public DbSet<PromotionRedemption> PromotionRedemptions => Set<PromotionRedemption>();
    public DbSet<Quote> Quotes => Set<Quote>();
    public DbSet<QuoteItem> QuoteItems => Set<QuoteItem>();
    public DbSet<HomepageBanner> HomepageBanners => Set<HomepageBanner>();

    public Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        return Database.BeginTransactionAsync(cancellationToken);
    }

    public AadhiDbContext(DbContextOptions<AadhiDbContext> options) : base(options)
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
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
            b.Property(p => p.Price).HasPrecision(18, 2);
            b.Property(p => p.CompareAtPrice).HasPrecision(18, 2);
            b.Property(p => p.CostPrice).HasPrecision(18, 2);
            b.Property(p => p.TaxRate).HasPrecision(5, 2);
            b.Property(p => p.RowVersion).IsConcurrencyToken();
            b.HasQueryFilter(p => !p.IsDeleted);

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

            b.HasMany(o => o.Items)
                .WithOne(oi => oi.Order)
                .HasForeignKey(oi => oi.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasMany(o => o.StatusHistories)
                .WithOne(h => h.Order)
                .HasForeignKey(h => h.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasMany(o => o.Invoices)
                .WithOne(i => i.Order)
                .HasForeignKey(i => i.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasMany(o => o.Payments)
                .WithOne(p => p.Order)
                .HasForeignKey(p => p.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasQueryFilter(o => !o.IsDeleted);
        });

        // OrderItem Configuration
        builder.Entity<OrderItem>(b =>
        {
            b.HasKey(oi => oi.Id);
            b.Property(oi => oi.ProductNameSnapshot).IsRequired().HasMaxLength(150);
            b.Property(oi => oi.SKUSnapshot).IsRequired().HasMaxLength(50);
        });

        // OrderStatusHistory Configuration
        builder.Entity<OrderStatusHistory>(b =>
        {
            b.HasKey(h => h.Id);
            b.HasIndex(h => h.OrderId);
            b.HasIndex(h => h.ChangedAtUtc);
        });

        // Warehouse Configuration
        builder.Entity<Warehouse>(b =>
        {
            b.HasKey(w => w.Id);
            b.HasIndex(w => w.Code).IsUnique();
            b.Property(w => w.Code).IsRequired().HasMaxLength(50);
            b.Property(w => w.Name).IsRequired().HasMaxLength(150);
        });

        // StockItem Configuration
        builder.Entity<StockItem>(b =>
        {
            b.HasKey(s => s.Id);
            b.HasIndex(s => new { s.ProductId, s.WarehouseId }).IsUnique();
            b.Property(s => s.RowVersion).IsConcurrencyToken();

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

        // ProductCategory Configuration
        builder.Entity<ProductCategory>(b =>
        {
            b.HasKey(pc => new { pc.ProductId, pc.CategoryId });

            b.HasOne(pc => pc.Product)
                .WithMany(p => p.ProductCategories)
                .HasForeignKey(pc => pc.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(pc => pc.Category)
                .WithMany(c => c.ProductCategories)
                .HasForeignKey(pc => pc.CategoryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ProductVariant Configuration
        builder.Entity<ProductVariant>(b =>
        {
            b.HasKey(pv => pv.Id);
            b.HasIndex(pv => pv.SKU).IsUnique();
            b.Property(pv => pv.SKU).IsRequired().HasMaxLength(50);
            b.Property(pv => pv.Name).IsRequired().HasMaxLength(150);

            b.HasOne(pv => pv.Product)
                .WithMany(p => p.Variants)
                .HasForeignKey(pv => pv.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // GiftBoxItem Configuration
        builder.Entity<GiftBoxItem>(b =>
        {
            b.HasKey(g => g.Id);
            b.HasIndex(g => new { g.ParentProductId, g.ComponentProductId }).IsUnique();

            b.HasOne(g => g.ParentProduct)
                .WithMany(p => p.BundleComponents)
                .HasForeignKey(g => g.ParentProductId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(g => g.ComponentProduct)
                .WithMany()
                .HasForeignKey(g => g.ComponentProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ProductReview Configuration
        builder.Entity<ProductReview>(b =>
        {
            b.HasKey(r => r.Id);
            b.HasIndex(r => r.ProductId);
            b.HasIndex(r => r.CreatedAtUtc);
            b.Property(r => r.Comment).HasMaxLength(1000);
            b.Property(r => r.CustomerName).HasMaxLength(100);
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

        // PurchaseOrderItem Configuration
        builder.Entity<PurchaseOrderItem>(b =>
        {
            b.HasKey(poi => poi.Id);
            b.HasOne(poi => poi.PurchaseOrder)
                .WithMany(po => po.Items)
                .HasForeignKey(poi => poi.PurchaseOrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // GoodsReceipt Configuration
        builder.Entity<GoodsReceipt>(b =>
        {
            b.HasKey(g => g.Id);
            b.HasIndex(g => g.ReceiptNumber).IsUnique();
            b.HasIndex(g => g.PurchaseOrderId);
            b.Property(g => g.ReceiptNumber).IsRequired().HasMaxLength(50);
        });

        // GoodsReceiptItem Configuration
        builder.Entity<GoodsReceiptItem>(b =>
        {
            b.HasKey(gi => gi.Id);
            b.HasOne(gi => gi.GoodsReceipt)
                .WithMany(g => g.Items)
                .HasForeignKey(gi => gi.GoodsReceiptId)
                .OnDelete(DeleteBehavior.Cascade);
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

        // Refund Configuration
        builder.Entity<Refund>(b =>
        {
            b.HasKey(r => r.Id);
            b.HasIndex(r => r.RefundNumber).IsUnique();
            b.HasIndex(r => r.OrderId);
            b.HasIndex(r => r.PaymentId);
        });

        // SupplierBill Configuration
        builder.Entity<SupplierBill>(b =>
        {
            b.HasKey(sb => sb.Id);
            b.HasIndex(sb => sb.BillNumber).IsUnique();
            b.HasIndex(sb => sb.SupplierId);
            b.HasIndex(sb => sb.PurchaseOrderId);
        });

        // ReturnOrder Configuration
        builder.Entity<ReturnOrder>(b =>
        {
            b.HasKey(ro => ro.Id);
            b.HasIndex(ro => ro.ReturnNumber).IsUnique();
            b.HasIndex(ro => ro.OrderId);
            b.HasIndex(ro => ro.CustomerId);
        });

        // ReturnOrderItem Configuration
        builder.Entity<ReturnOrderItem>(b =>
        {
            b.HasKey(ri => ri.Id);
            b.HasOne(ri => ri.ReturnOrder)
                .WithMany(ro => ro.Items)
                .HasForeignKey(ri => ri.ReturnOrderId)
                .OnDelete(DeleteBehavior.Cascade);
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
            b.Property(p => p.RowVersion).IsConcurrencyToken();
            b.HasQueryFilter(p => !p.IsDeleted);
        });

        // PromotionRedemption Configuration
        builder.Entity<PromotionRedemption>(b =>
        {
            b.HasKey(r => r.Id);
            b.HasIndex(r => new { r.PromotionId, r.CustomerId });
            b.HasIndex(r => r.OrderId);
            b.Property(r => r.RowVersion).IsConcurrencyToken();
            b.HasOne(r => r.Promotion)
                .WithMany(p => p.Redemptions)
                .HasForeignKey(r => r.PromotionId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(r => r.Customer)
                .WithMany()
                .HasForeignKey(r => r.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(r => r.Order)
                .WithMany()
                .HasForeignKey(r => r.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasQueryFilter(r => !r.IsDeleted);
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

        // LoginHistory Configuration
        builder.Entity<LoginHistory>(b =>
        {
            b.HasKey(l => l.Id);
            b.HasIndex(l => l.TimestampUtc);
            b.HasIndex(l => l.UserId);
            b.HasIndex(l => l.Email);
        });

        // RateLimitLog Configuration
        builder.Entity<RateLimitLog>(b =>
        {
            b.HasKey(r => r.Id);
            b.HasIndex(r => r.TimestampUtc);
            b.HasIndex(r => r.Endpoint);
            b.HasIndex(r => r.IpAddress);
        });

        // Quote Configuration
        builder.Entity<Quote>(b =>
        {
            b.HasKey(q => q.Id);
            b.HasIndex(q => q.QuoteNumber).IsUnique();
            b.HasIndex(q => q.CustomerId);
            b.HasIndex(q => q.Status);
            b.Property(q => q.QuoteNumber).IsRequired().HasMaxLength(50);
            b.Property(q => q.Subtotal).HasPrecision(18, 2);
            b.Property(q => q.Discount).HasPrecision(18, 2);
            b.Property(q => q.Tax).HasPrecision(18, 2);
            b.Property(q => q.GrandTotal).HasPrecision(18, 2);
            b.HasMany(q => q.Items).WithOne(i => i.Quote).HasForeignKey(i => i.QuoteId).OnDelete(DeleteBehavior.Cascade);
            b.HasQueryFilter(q => !q.IsDeleted);
        });

        // QuoteItem Configuration
        builder.Entity<QuoteItem>(b =>
        {
            b.HasKey(qi => qi.Id);
            b.Property(qi => qi.UnitPrice).HasPrecision(18, 2);
            b.Property(qi => qi.LineTotal).HasPrecision(18, 2);
            b.Property(qi => qi.DiscountPercentage).HasPrecision(18, 2);
        });

        // HomepageBanner Configuration
        builder.Entity<HomepageBanner>(b =>
        {
            b.HasKey(bn => bn.Id);
            b.HasIndex(bn => bn.IsActive);
            b.HasIndex(bn => bn.DisplayOrder);
            b.Property(bn => bn.Title).IsRequired().HasMaxLength(150);
            b.Property(bn => bn.ImageUrl).IsRequired().HasMaxLength(500);
            b.Property(bn => bn.TargetUrl).HasMaxLength(500);
            b.Property(bn => bn.CtaText).HasMaxLength(50);
            b.HasQueryFilter(bn => !bn.IsDeleted);
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is BaseEntity<Guid> baseEntity)
            {
                if (entry.State == EntityState.Added)
                {
                    if (baseEntity.Id == Guid.Empty)
                    {
                        baseEntity.Id = Guid.NewGuid();
                    }
                    if (baseEntity.CreatedAtUtc == default)
                    {
                        baseEntity.CreatedAtUtc = DateTime.UtcNow;
                    }
                }
                else if (entry.State == EntityState.Modified)
                {
                    baseEntity.UpdatedAtUtc = DateTime.UtcNow;
                }
            }

            if (entry.State == EntityState.Modified)
            {
                if (entry.Entity is Product product)
                {
                    product.RowVersion = Guid.NewGuid();
                }
                else if (entry.Entity is StockItem stockItem)
                {
                    stockItem.RowVersion = Guid.NewGuid();
                }
                else if (entry.Entity is Promotion promotion)
                {
                    promotion.RowVersion = Guid.NewGuid();
                }
                else if (entry.Entity is PromotionRedemption redemption)
                {
                    redemption.RowVersion = Guid.NewGuid();
                }
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
