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
    public DbSet<ProductComboItem> ProductComboItems => Set<ProductComboItem>();
    public DbSet<ProductReview> ProductReviews => Set<ProductReview>();
    public DbSet<PromotionRedemption> PromotionRedemptions => Set<PromotionRedemption>();
    public DbSet<HomepageBanner> HomepageBanners => Set<HomepageBanner>();
    public DbSet<OtpVerification> OtpVerifications => Set<OtpVerification>();
    public DbSet<WishlistItem> WishlistItems => Set<WishlistItem>();

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
            b.Property(o => o.PackingChargePercent).HasPrecision(5, 2);

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

        // ProductComboItem Configuration — combo / gift-box composition
        builder.Entity<ProductComboItem>(b =>
        {
            b.HasKey(ci => ci.Id);
            b.HasIndex(ci => ci.ComboProductId);
            b.HasIndex(ci => new { ci.ComboProductId, ci.ComponentProductId }).IsUnique();

            // Deleting the combo removes its composition rows...
            b.HasOne(ci => ci.ComboProduct)
                .WithMany(p => p.ComboItems)
                .HasForeignKey(ci => ci.ComboProductId)
                .OnDelete(DeleteBehavior.Cascade);

            // ...but a component product must never be deleted out from under a combo.
            b.HasOne(ci => ci.ComponentProduct)
                .WithMany()
                .HasForeignKey(ci => ci.ComponentProductId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasQueryFilter(ci => !ci.IsDeleted);
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

        // OtpVerification Configuration
        builder.Entity<OtpVerification>(b =>
        {
            b.HasKey(o => o.Id);
            b.HasIndex(o => o.UserId);
            b.HasIndex(o => o.ResetToken);
            b.HasIndex(o => o.ExpiresAtUtc);
            b.Property(o => o.UserId).IsRequired().HasMaxLength(64);
            b.Property(o => o.Code).IsRequired().HasMaxLength(10);
            b.Property(o => o.Purpose).IsRequired().HasMaxLength(50);
            b.Property(o => o.ResetToken).HasMaxLength(64);
        });

        // WishlistItem Configuration
        builder.Entity<WishlistItem>(b =>
        {
            b.HasKey(w => w.Id);
            b.HasIndex(w => new { w.CustomerId, w.ProductId }).IsUnique();

            b.HasOne(w => w.Customer)
                .WithMany()
                .HasForeignKey(w => w.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(w => w.Product)
                .WithMany()
                .HasForeignKey(w => w.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasQueryFilter(w => !w.IsDeleted);
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
            b.Property(bn => bn.Placement).IsRequired().HasMaxLength(20).HasDefaultValue("Home");
            b.HasIndex(bn => bn.Placement);
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
