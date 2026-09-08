using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Application.Services;
using AadhiCrackers.Contracts.Orders;
using AadhiCrackers.Domain.Entities;
using AadhiCrackers.Domain.Enums;
using AadhiCrackers.Domain.Exceptions;
using AadhiCrackers.Domain.ValueObjects;
using AadhiCrackers.Infrastructure.Persistence;
using AadhiCrackers.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AadhiCrackers.Infrastructure.Tests;

public class OrderStockLifecycleAndConcurrencyTests
{
    [Fact]
    public async Task OrderCreation_ReservesStockDirectlyOnProduct()
    {
        var dbPath = CreateTempDbPath();
        try
        {
            Guid orderId;
            Guid productId;

            await using (var context = new AadhiDbContext(CreateSqliteOptions(dbPath)))
            {
                await DatabaseInitializer.InitializeAsync(context, NullLogger.Instance);
                var (category, product, customer) = await SeedBaseDataAsync(context);
                productId = product.Id;

                var orderService = CreateOrderService(context);

                var request = new CreateOrderRequest
                {
                    ShippingAddress = new Address("Arun Kumar", "9876543210", "123 Main St", null, "Sivakasi", "TN", "626123"),
                    PaymentMethod = PaymentMethod.UPI,
                    Items = new List<CreateOrderItemRequest>
                    {
                        new() { ProductId = product.Id, Quantity = 3 }
                    }
                };

                var orderDto = await orderService.CreateOrderAsync(request);
                orderDto.Should().NotBeNull();
                orderDto.GrandTotal.Should().BeGreaterThan(0);
                orderId = orderDto.Id;
            }

            // Verify in fresh DbContext
            await using (var context = new AadhiDbContext(CreateSqliteOptions(dbPath)))
            {
                var updatedProduct = await context.Products.FirstAsync(p => p.Id == productId);
                updatedProduct.StockQuantity.Should().Be(10);
                updatedProduct.ReservedQuantity.Should().Be(3);
                updatedProduct.AvailableQuantity.Should().Be(7);
            }
        }
        finally
        {
            DeleteDb(dbPath);
        }
    }

    [Fact]
    public async Task OrderCancellation_ReleasesReservedStockDirectlyOnProduct()
    {
        var dbPath = CreateTempDbPath();
        try
        {
            Guid orderId;
            Guid productId;

            await using (var context = new AadhiDbContext(CreateSqliteOptions(dbPath)))
            {
                await DatabaseInitializer.InitializeAsync(context, NullLogger.Instance);
                var (category, product, customer) = await SeedBaseDataAsync(context);
                productId = product.Id;

                var orderService = CreateOrderService(context);
                var order = await orderService.CreateOrderAsync(new CreateOrderRequest
                {
                    ShippingAddress = new Address("Arun Kumar", "9876543210", "123 Main St", null, "Sivakasi", "TN", "626123"),
                    Items = new List<CreateOrderItemRequest> { new() { ProductId = product.Id, Quantity = 4 } }
                });
                orderId = order.Id;
            }

            // Cancel Order in new scope
            await using (var context = new AadhiDbContext(CreateSqliteOptions(dbPath)))
            {
                var orderService = CreateOrderService(context);
                await orderService.UpdateOrderStatusAsync(orderId, new UpdateOrderStatusRequest
                {
                    NewStatus = OrderStatus.Cancelled,
                    Reason = "Customer requested cancellation"
                });
            }

            // Verify Stock released
            await using (var context = new AadhiDbContext(CreateSqliteOptions(dbPath)))
            {
                var updatedProduct = await context.Products.FirstAsync(p => p.Id == productId);
                updatedProduct.StockQuantity.Should().Be(10);
                updatedProduct.ReservedQuantity.Should().Be(0);
                updatedProduct.AvailableQuantity.Should().Be(10);
            }
        }
        finally
        {
            DeleteDb(dbPath);
        }
    }

    [Fact]
    public async Task OrderShipment_DeductsDirectStockOnProduct()
    {
        var dbPath = CreateTempDbPath();
        try
        {
            Guid orderId;
            Guid productId;

            await using (var context = new AadhiDbContext(CreateSqliteOptions(dbPath)))
            {
                await DatabaseInitializer.InitializeAsync(context, NullLogger.Instance);
                var (category, product, customer) = await SeedBaseDataAsync(context);
                productId = product.Id;

                var orderService = CreateOrderService(context);
                var order = await orderService.CreateOrderAsync(new CreateOrderRequest
                {
                    ShippingAddress = new Address("Arun Kumar", "9876543210", "123 Main St", null, "Sivakasi", "TN", "626123"),
                    Items = new List<CreateOrderItemRequest> { new() { ProductId = product.Id, Quantity = 3 } }
                });
                orderId = order.Id;
            }

            // Move Pending -> Confirmed -> Processing -> Packed -> Shipped
            foreach (var status in new[] { OrderStatus.Confirmed, OrderStatus.Processing, OrderStatus.Packed, OrderStatus.Shipped })
            {
                await using var context = new AadhiDbContext(CreateSqliteOptions(dbPath));
                var orderService = CreateOrderService(context);
                await orderService.UpdateOrderStatusAsync(orderId, new UpdateOrderStatusRequest { NewStatus = status });
            }

            // Verify Stock deduction
            await using (var context = new AadhiDbContext(CreateSqliteOptions(dbPath)))
            {
                var updatedProduct = await context.Products.FirstAsync(p => p.Id == productId);
                updatedProduct.StockQuantity.Should().Be(7);
                updatedProduct.ReservedQuantity.Should().Be(0);
                updatedProduct.AvailableQuantity.Should().Be(7);
            }

            // Move Shipped -> OutForDelivery -> Delivered
            foreach (var status in new[] { OrderStatus.OutForDelivery, OrderStatus.Delivered })
            {
                await using var context = new AadhiDbContext(CreateSqliteOptions(dbPath));
                var orderService = CreateOrderService(context);
                await orderService.UpdateOrderStatusAsync(orderId, new UpdateOrderStatusRequest { NewStatus = status });
            }

            // Ensure no double-deduction on delivery
            await using (var context = new AadhiDbContext(CreateSqliteOptions(dbPath)))
            {
                var finalProduct = await context.Products.FirstAsync(p => p.Id == productId);
                finalProduct.StockQuantity.Should().Be(7);
                finalProduct.ReservedQuantity.Should().Be(0);
                finalProduct.AvailableQuantity.Should().Be(7);
            }
        }
        finally
        {
            DeleteDb(dbPath);
        }
    }

    [Fact]
    public async Task PaymentRejection_ReleasesReservationDirectlyOnProduct()
    {
        var dbPath = CreateTempDbPath();
        try
        {
            Guid orderId;
            Guid productId;

            await using (var context = new AadhiDbContext(CreateSqliteOptions(dbPath)))
            {
                await DatabaseInitializer.InitializeAsync(context, NullLogger.Instance);
                var (category, product, customer) = await SeedBaseDataAsync(context);
                productId = product.Id;

                var orderService = CreateOrderService(context);
                var order = await orderService.CreateOrderAsync(new CreateOrderRequest
                {
                    ShippingAddress = new Address("Arun Kumar", "9876543210", "123 Main St", null, "Sivakasi", "TN", "626123"),
                    Items = new List<CreateOrderItemRequest> { new() { ProductId = product.Id, Quantity = 3 } }
                });
                orderId = order.Id;
            }

            // Reject payment
            await using (var context = new AadhiDbContext(CreateSqliteOptions(dbPath)))
            {
                var orderService = CreateOrderService(context);
                var updated = await orderService.RejectPaymentAsync(orderId, new RejectPaymentRequest { Reason = "Invalid screenshot" });
                updated.OrderStatus.Should().Be(OrderStatus.Cancelled);
                updated.PaymentStatus.Should().Be(PaymentStatus.Failed);
            }

            // Verify stock reservation released on product
            await using (var context = new AadhiDbContext(CreateSqliteOptions(dbPath)))
            {
                var product = await context.Products.FirstAsync(p => p.Id == productId);
                product.StockQuantity.Should().Be(10);
                product.ReservedQuantity.Should().Be(0);
                product.AvailableQuantity.Should().Be(10);

                var audit = await context.AuditLogs.FirstOrDefaultAsync(a => a.Action == AuditAction.PaymentRejected);
                audit.Should().NotBeNull();
            }
        }
        finally
        {
            DeleteDb(dbPath);
        }
    }

    [Fact]
    public async Task MoveToPacking_ThrowsInvalidOrderStateTransition_OnUnconfirmedOrder()
    {
        var dbPath = CreateTempDbPath();
        try
        {
            Guid orderId;
            await using (var context = new AadhiDbContext(CreateSqliteOptions(dbPath)))
            {
                await DatabaseInitializer.InitializeAsync(context, NullLogger.Instance);
                var (category, product, customer) = await SeedBaseDataAsync(context);

                var orderService = CreateOrderService(context);
                var order = await orderService.CreateOrderAsync(new CreateOrderRequest
                {
                    ShippingAddress = new Address("Arun Kumar", "9876543210", "123 Main St", null, "Sivakasi", "TN", "626123"),
                    Items = new List<CreateOrderItemRequest> { new() { ProductId = product.Id, Quantity = 1 } }
                });
                orderId = order.Id;
            }

            await using (var context = new AadhiDbContext(CreateSqliteOptions(dbPath)))
            {
                var orderService = CreateOrderService(context);
                // MoveToPacking requires Confirmed or Processing; Pending should fail
                var act = async () => await orderService.MoveToPackingAsync(orderId);
                await act.Should().ThrowAsync<InvalidOrderStateTransitionException>();
            }
        }
        finally
        {
            DeleteDb(dbPath);
        }
    }

    private static OrderService CreateOrderService(AadhiDbContext context)
    {
        var user = new TestCurrentUserService();
        var outbox = new OutboxService(context);
        var audit = new AuditLogService(context, user, outbox);
        var numberGen = new BusinessNumberGenerator(context);
        return new OrderService(context, user, audit, outbox, numberGen);
    }

    private static async Task<(Category, Product, Customer)> SeedBaseDataAsync(AadhiDbContext context)
    {
        var category = new Category { Name = "Crackers", Slug = "crackers", IsActive = true };
        context.Categories.Add(category);

        var product = new Product
        {
            Name = "Sparklers 10cm",
            SKU = "SPK-010",
            Slug = "sparklers-10cm",
            CategoryId = category.Id,
            Price = Money.FromDecimal(100m),
            TaxRate = 18m,
            StockQuantity = 10,
            ReservedQuantity = 0,
            IsActive = true
        };
        context.Products.Add(product);

        var customer = new Customer
        {
            FirstName = "Arun",
            LastName = "Kumar",
            Email = "customer@aadhicrackers.com",
            Phone = "9876543210",
            CustomerCode = "CUST-001"
        };
        context.Customers.Add(customer);

        await context.SaveChangesAsync();
        return (category, product, customer);
    }

    private static string CreateTempDbPath() => Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");

    private static DbContextOptions<AadhiDbContext> CreateSqliteOptions(string databasePath) =>
        new DbContextOptionsBuilder<AadhiDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;

    private static void DeleteDb(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }

    private sealed class TestCurrentUserService : ICurrentUserService
    {
        public string? UserId => "test-user-id";
        public string? Email => "admin@aadhicrackers.com";
        public string? UserName => "admin";
        public string? Role => "SuperAdmin";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "TestRunner";
        public string CorrelationId => Guid.NewGuid().ToString();
        public bool IsAuthenticated => true;
    }
}
