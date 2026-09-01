using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Application.Services;
using AadhiCrackers.Contracts.Catalog;
using AadhiCrackers.Contracts.Marketing;
using AadhiCrackers.Domain.Entities;
using AadhiCrackers.Domain.Enums;
using AadhiCrackers.Domain.Exceptions;
using AadhiCrackers.Domain.ValueObjects;
using AadhiCrackers.Infrastructure.Persistence;
using AadhiCrackers.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AadhiCrackers.Infrastructure.Tests;

public class ReviewsAndBannersWorkflowTests
{
    private static string CreateTempDbPath() =>
        Path.Combine(Path.GetTempPath(), $"aadhi_rev_ban_test_{Guid.NewGuid():N}.db");

    private static DbContextOptions<AadhiDbContext> CreateSqliteOptions(string dbPath) =>
        new DbContextOptionsBuilder<AadhiDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

    private static void DeleteDb(string path)
    {
        try
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    private static (ReviewService reviewService, BannerService bannerService, AadhiDbContext context) CreateServices(string dbPath, string role = "Admin", string? userId = null)
    {
        var context = new AadhiDbContext(CreateSqliteOptions(dbPath));
        var user = new TestCurrentUserService(role, userId);
        var outbox = new OutboxService(context);
        var audit = new AuditLogService(context, user, outbox);

        var reviewService = new ReviewService(context, user, audit);
        var bannerService = new BannerService(context, audit);

        return (reviewService, bannerService, context);
    }

    [Fact]
    public async Task SubmitReview_EligibleCustomer_CreatesPendingReview_AndAllowsAdminModeration()
    {
        var dbPath = CreateTempDbPath();
        try
        {
            await using (var context = new AadhiDbContext(CreateSqliteOptions(dbPath)))
            {
                await DatabaseInitializer.InitializeAsync(context, NullLogger.Instance);

                var (product, customer) = await SeedProductAndCustomerAsync(context);

                // Create a delivered order for this customer with this product
                var order = new Order
                {
                    OrderNumber = "ORD-2026-0001",
                    CustomerId = customer.Id,
                    Customer = customer,
                    OrderStatus = OrderStatus.Delivered,
                    PaymentStatus = PaymentStatus.Paid,
                    ShippingAddress = new Address("Customer", "9876543210", "123 Main", null, "SVK", "TN", "626123")
                };
                order.Items.Add(new OrderItem
                {
                    OrderId = order.Id,
                    ProductId = product.Id,
                    Product = product,
                    ProductNameSnapshot = product.Name,
                    SKUSnapshot = product.SKU,
                    Quantity = 2,
                    UnitPrice = product.Price,
                    LineTotal = product.Price * 2
                });
                context.Orders.Add(order);
                await context.SaveChangesAsync();
            }

            // Customer submits review
            Guid reviewId;
            await using (var context = new AadhiDbContext(CreateSqliteOptions(dbPath)))
            {
                var (reviewService, _, _) = CreateServices(dbPath, role: "Customer", userId: "user-cust-123");
                var product = await context.Products.FirstAsync();

                var review = await reviewService.CreateReviewAsync(new CreateProductReviewRequest
                {
                    ProductId = product.Id,
                    Rating = 5,
                    Title = "Spectacular Sparklers!",
                    Comment = "Very bright and long lasting. Highly recommended for Diwali!",
                    CustomerName = "Verified Customer"
                });

                review.Should().NotBeNull();
                review.Rating.Should().Be(5);
                review.Status.Should().Be("Pending");
                reviewId = review.Id;
            }

            // Admin moderates review to Approved
            await using (var context = new AadhiDbContext(CreateSqliteOptions(dbPath)))
            {
                var (reviewService, _, _) = CreateServices(dbPath, role: "Admin", userId: "admin-1");

                var updated = await reviewService.UpdateReviewStatusAsync(reviewId, new UpdateReviewStatusRequest
                {
                    Status = "Approved"
                });

                updated.Status.Should().Be("Approved");

                var publicReviews = await reviewService.GetReviewsAsync(status: "Approved");
                publicReviews.Items.Should().ContainSingle(r => r.Id == reviewId);
            }
        }
        finally
        {
            DeleteDb(dbPath);
        }
    }

    [Fact]
    public async Task SubmitReview_CustomerWithoutPurchase_ThrowsDomainException()
    {
        var dbPath = CreateTempDbPath();
        try
        {
            await using (var context = new AadhiDbContext(CreateSqliteOptions(dbPath)))
            {
                await DatabaseInitializer.InitializeAsync(context, NullLogger.Instance);
                await SeedProductAndCustomerAsync(context);
            }

            await using (var context = new AadhiDbContext(CreateSqliteOptions(dbPath)))
            {
                var (reviewService, _, _) = CreateServices(dbPath, role: "Customer", userId: "user-cust-123");
                var product = await context.Products.FirstAsync();

                var act = async () => await reviewService.CreateReviewAsync(new CreateProductReviewRequest
                {
                    ProductId = product.Id,
                    Rating = 4,
                    Comment = "Nice product"
                });

                await act.Should().ThrowAsync<DomainException>()
                    .WithMessage("*delivered order*");
            }
        }
        finally
        {
            DeleteDb(dbPath);
        }
    }

    [Fact]
    public async Task HomepageBanners_CRUD_FiltersActiveAndDateWindowCorrectly()
    {
        var dbPath = CreateTempDbPath();
        try
        {
            await using (var context = new AadhiDbContext(CreateSqliteOptions(dbPath)))
            {
                await DatabaseInitializer.InitializeAsync(context, NullLogger.Instance);
            }

            Guid activeBannerId;
            await using (var context = new AadhiDbContext(CreateSqliteOptions(dbPath)))
            {
                var (_, bannerService, _) = CreateServices(dbPath, role: "Admin");

                // Create live active banner
                var liveBanner = await bannerService.CreateBannerAsync(new CreateHomepageBannerRequest
                {
                    Title = "Diwali Mega Sale",
                    Subtitle = "Flat 50% Off On All Gift Boxes",
                    ImageUrl = "https://example.com/banner1.jpg",
                    TargetUrl = "/categories/gift-boxes",
                    CtaText = "Shop Boxes",
                    DisplayOrder = 1,
                    IsActive = true,
                    StartDateUtc = DateTime.UtcNow.AddDays(-1),
                    EndDateUtc = DateTime.UtcNow.AddDays(10)
                });
                activeBannerId = liveBanner.Id;

                // Create expired banner
                await bannerService.CreateBannerAsync(new CreateHomepageBannerRequest
                {
                    Title = "Expired Promo",
                    ImageUrl = "https://example.com/expired.jpg",
                    IsActive = true,
                    StartDateUtc = DateTime.UtcNow.AddDays(-20),
                    EndDateUtc = DateTime.UtcNow.AddDays(-5)
                });

                // Create inactive banner
                await bannerService.CreateBannerAsync(new CreateHomepageBannerRequest
                {
                    Title = "Draft Banner",
                    ImageUrl = "https://example.com/draft.jpg",
                    IsActive = false
                });

                // Active only query
                var activeBanners = await bannerService.GetBannersAsync(activeOnly: true);
                activeBanners.Should().HaveCount(1);
                activeBanners.First().Id.Should().Be(activeBannerId);

                // Admin all query
                var allBanners = await bannerService.GetBannersAsync(activeOnly: false);
                allBanners.Should().HaveCount(3);
            }
        }
        finally
        {
            DeleteDb(dbPath);
        }
    }

    private static async Task<(Product product, Customer customer)> SeedProductAndCustomerAsync(AadhiDbContext context)
    {
        var category = new Category { Name = "Sparklers", Slug = "sparklers", IsActive = true };
        context.Categories.Add(category);

        var product = new Product
        {
            Name = "Deluxe Sparkler 15cm",
            SKU = "SPK-DLX-15",
            Slug = "deluxe-sparkler-15cm",
            CategoryId = category.Id,
            Price = Money.FromDecimal(150m),
            StockQuantity = 50,
            IsActive = true
        };
        context.Products.Add(product);

        var customer = new Customer
        {
            UserId = "user-cust-123",
            FirstName = "Karthik",
            LastName = "Raja",
            Email = "karthik@example.com",
            Phone = "9876543210",
            IsActive = true
        };
        context.Customers.Add(customer);

        await context.SaveChangesAsync();
        return (product, customer);
    }

    private sealed class TestCurrentUserService : ICurrentUserService
    {
        public string? UserId { get; }
        public string? UserName => "TestUser";
        public string? Email => "user@example.com";
        public string? Role { get; }
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "TestRunner";
        public string CorrelationId => "test-correlation";
        public bool IsAuthenticated => true;

        public TestCurrentUserService(string role = "Admin", string? userId = null)
        {
            Role = role;
            UserId = userId ?? "admin-user-id";
        }
    }
}
