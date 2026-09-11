using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Application.Services;
using AadhiCrackers.Application.Validators;
using AadhiCrackers.Contracts.Catalog;
using AadhiCrackers.Contracts.Orders;
using AadhiCrackers.Domain.Entities;
using AadhiCrackers.Domain.Enums;
using AadhiCrackers.Domain.ValueObjects;
using AadhiCrackers.Infrastructure.Persistence;
using AadhiCrackers.Infrastructure.Services;
using FluentValidation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AadhiCrackers.Infrastructure.Tests;

public class ValidationAndAuthorizationFixTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AadhiDbContext> _options;

    public ValidationAndAuthorizationFixTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<AadhiDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new AadhiDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private sealed class TestCurrentUserService : ICurrentUserService
    {
        public string? UserId { get; set; } = Guid.NewGuid().ToString();
        public string? Email { get; set; } = "customer@aadhicracker.in";
        public string? UserName { get; set; } = "testCustomer";
        public string? Role { get; set; } = "Customer";
        public string? IpAddress { get; set; } = "192.168.1.100";
        public string? UserAgent { get; set; } = "Mozilla/5.0 TestAgent";
        public string CorrelationId { get; set; } = Guid.NewGuid().ToString("N");
        public bool IsAuthenticated => true;
    }

    [Fact]
    public async Task FluentValidation_CreateProductValidator_FailsOnInvalidSKUOrPrice()
    {
        // Arrange
        var validator = new CreateProductRequestValidator();
        var invalidRequest = new CreateProductRequest
        {
            SKU = "", // Invalid
            Name = "", // Invalid
            Price = -50m, // Invalid negative price
            CategoryId = Guid.Empty
        };

        // Act
        var result = await validator.ValidateAsync(invalidRequest);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateProductRequest.SKU));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateProductRequest.Name));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateProductRequest.Price));
    }

    [Fact]
    public async Task CustomerOrderIsolation_GetMyOrders_ReturnsOnlyAuthenticatedCustomerOrders()
    {
        // Arrange
        var customerAId = Guid.NewGuid();
        var customerBId = Guid.NewGuid();
        var userAId = Guid.NewGuid().ToString();
        var userBId = Guid.NewGuid().ToString();

        using (var context = new AadhiDbContext(_options))
        {
            var customerA = new Customer
            {
                Id = customerAId,
                UserId = userAId,
                CustomerCode = "CUST-A",
                FirstName = "Alice",
                LastName = "Smith",
                Email = "alice@example.com",
                Phone = "9876543210"
            };

            var customerB = new Customer
            {
                Id = customerBId,
                UserId = userBId,
                CustomerCode = "CUST-B",
                FirstName = "Bob",
                LastName = "Jones",
                Email = "bob@example.com",
                Phone = "9876543211"
            };

            context.Customers.AddRange(customerA, customerB);

            var orderA = new Order
            {
                Id = Guid.NewGuid(),
                OrderNumber = "ORD-ALICE-1",
                CustomerId = customerAId,
                OrderStatus = OrderStatus.Pending,
                PaymentStatus = PaymentStatus.Pending,
                GrandTotal = Money.FromDecimal(500)
            };

            var orderB = new Order
            {
                Id = Guid.NewGuid(),
                OrderNumber = "ORD-BOB-1",
                CustomerId = customerBId,
                OrderStatus = OrderStatus.Pending,
                PaymentStatus = PaymentStatus.Pending,
                GrandTotal = Money.FromDecimal(800)
            };

            context.Orders.AddRange(orderA, orderB);
            await context.SaveChangesAsync();
        }

        var currentUserA = new TestCurrentUserService { UserId = userAId, Email = "alice@example.com", Role = "Customer" };

        using (var context = new AadhiDbContext(_options))
        {
            var auditLog = new AuditLogService(context, currentUserA, new OutboxService(context));
            var orderService = new OrderService(context, currentUserA, auditLog, new OutboxService(context), new BusinessNumberGenerator(context));

            // Act
            var aliceOrders = await orderService.GetMyOrdersAsync();

            // Assert
            Assert.Single(aliceOrders);
            Assert.Equal("ORD-ALICE-1", aliceOrders[0].OrderNumber);
            Assert.Equal(500m, aliceOrders[0].GrandTotal);
        }
    }

    [Fact]
    public async Task LocalFileStorageService_ThrowsException_OnDisallowedExtension()
    {
        // Arrange
        var storageService = new LocalFileStorageService();
        using var stream = new MemoryStream(new byte[] { 0x01, 0x02, 0x03 });

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            storageService.SaveFileAsync(stream, "dangerous_script.exe", "test"));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            storageService.SaveFileAsync(stream, "exploit.bat", "test"));
    }

    [Fact]
    public async Task SearchService_CalculatesRealRatings_FromApprovedReviews()
    {
        // Arrange
        var categoryId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        using (var context = new AadhiDbContext(_options))
        {
            var category = new Category { Id = categoryId, Name = "Rockets", Slug = "rockets" };
            var product = new Product
            {
                Id = productId,
                Name = "Sky Shot 500",
                SKU = "SKY-500",
                Slug = "sky-shot-500",
                CategoryId = categoryId,
                Price = Money.FromDecimal(1200),
                StockQuantity = 50,
                IsActive = true
            };

            var review1 = new ProductReview
            {
                Id = Guid.NewGuid(),
                ProductId = productId,
                Rating = 5,
                Status = "Approved",
                CustomerName = "Reviewer 1",
                Comment = "Amazing fireworks"
            };

            var review2 = new ProductReview
            {
                Id = Guid.NewGuid(),
                ProductId = productId,
                Rating = 4,
                Status = "Approved",
                CustomerName = "Reviewer 2",
                Comment = "Good quality"
            };

            var unapprovedReview = new ProductReview
            {
                Id = Guid.NewGuid(),
                ProductId = productId,
                Rating = 1,
                Status = "Pending", // Should not be counted
                CustomerName = "Reviewer 3",
                Comment = "Pending"
            };

            context.Categories.Add(category);
            context.Products.Add(product);
            context.ProductReviews.AddRange(review1, review2, unapprovedReview);
            await context.SaveChangesAsync();
        }

        using (var context = new AadhiDbContext(_options))
        {
            var searchService = new SearchService(context);

            // Act
            var searchResult = await searchService.SearchProductsAsync("Sky Shot");

            // Assert
            Assert.Single(searchResult.Items);
            var item = searchResult.Items[0];
            Assert.Equal("Sky Shot 500", item.Name);
            Assert.Equal(4.5, item.Rating); // (5 + 4) / 2 = 4.5
            Assert.Equal(2, item.ReviewCount); // Only 2 approved reviews
        }
    }

    [Fact]
    public async Task RateLimitLog_PersistsToDatabase_Correctly()
    {
        // Arrange
        using var context = new AadhiDbContext(_options);
        var log = new RateLimitLog
        {
            Id = Guid.NewGuid(),
            TimestampUtc = DateTime.UtcNow,
            Endpoint = "/api/v1/auth/login",
            Policy = "LOGIN",
            IpAddress = "192.168.1.50",
            RequestsCount = 6,
            BlockedCount = 1,
            Reason = "Rate limit threshold exceeded"
        };

        // Act
        context.RateLimitLogs.Add(log);
        await context.SaveChangesAsync();

        // Assert
        var saved = await context.RateLimitLogs.FirstOrDefaultAsync(l => l.Id == log.Id);
        Assert.NotNull(saved);
        Assert.Equal("LOGIN", saved.Policy);
        Assert.Equal("192.168.1.50", saved.IpAddress);
        Assert.Equal(1, saved.BlockedCount);
    }

    [Fact]
    public async Task Outbox_EnqueuedMessages_ArePersistedWithPendingStatus()
    {
        // Arrange
        using var context = new AadhiDbContext(_options);
        var outbox = new OutboxService(context);

        // Act
        await outbox.EnqueueAsync("PaymentVerified", new { OrderId = Guid.NewGuid(), OrderNumber = "ORD-TEST-100" });
        await context.SaveChangesAsync();

        // Assert
        var msg = await context.OutboxMessages.FirstOrDefaultAsync(m => m.Type == "PaymentVerified");
        Assert.NotNull(msg);
        Assert.Equal("Pending", msg.Status);
        Assert.Equal(0, msg.RetryCount);
    }
}
