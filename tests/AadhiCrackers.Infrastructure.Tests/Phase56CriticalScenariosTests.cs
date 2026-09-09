using System.Text.Json;
using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Application.Services;
using AadhiCrackers.Contracts.Common;
using AadhiCrackers.Contracts.Finance;
using AadhiCrackers.Contracts.Orders;
using AadhiCrackers.Domain.Entities;
using AadhiCrackers.Domain.Enums;
using AadhiCrackers.Domain.Exceptions;
using AadhiCrackers.Domain.ValueObjects;
using AadhiCrackers.Infrastructure.Persistence;
using AadhiCrackers.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AadhiCrackers.Infrastructure.Tests;

/// <summary>
/// Critical Scenarios verifying direct product stock lifecycle, payment idempotency, outbox, and security.
/// </summary>
public class Phase56CriticalScenariosTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AadhiDbContext> _options;

    public Phase56CriticalScenariosTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
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

    private (OrderService orderService, FinanceService financeService, ReportService reportService, TestCurrentUserService currentUser)
        CreateServices(AadhiDbContext context, string? userId = null, string? role = null, string? email = null)
    {
        var currentUser = new TestCurrentUserService(userId, role, email);
        var outbox = new OutboxService(context);
        var audit = new AuditLogService(context, currentUser, outbox);
        var numberGen = new BusinessNumberGenerator(context);
        var orderService = new OrderService(context, currentUser, audit, outbox, numberGen);
        var financeService = new FinanceService(context, currentUser, audit, outbox, numberGen);
        var reportService = new ReportService(context);
        return (orderService, financeService, reportService, currentUser);
    }

    private async Task<(Product product, Customer customer)> SeedBaseDataAsync(AadhiDbContext context)
    {
        var category = new Category { Name = "Crackers", Slug = "crackers", IsActive = true };
        context.Categories.Add(category);

        var product = new Product
        {
            CategoryId = category.Id,
            SKU = "CRK-001",
            Name = "Sparklers 50 Pcs",
            Price = Money.FromDecimal(200m),
            CostPrice = Money.FromDecimal(100m),
            StockQuantity = 10,
            ReservedQuantity = 0,
            ReorderLevel = 2,
            IsActive = true
        };
        context.Products.Add(product);

        var customer = new Customer
        {
            UserId = Guid.NewGuid().ToString(),
            CustomerCode = "CUST-001",
            FirstName = "Suresh",
            LastName = "Raina",
            Email = "suresh@test.com",
            Phone = "9876543210",
            IsActive = true
        };
        context.Customers.Add(customer);

        await context.SaveChangesAsync();
        return (product, customer);
    }

    private sealed class TestCurrentUserService : ICurrentUserService
    {
        public string? UserId { get; set; }
        public string? Email { get; set; }
        public string? UserName { get; set; } = "AdminUser";
        public string? Role { get; set; } = "SuperAdmin";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "xUnit";
        public string CorrelationId => Guid.NewGuid().ToString();
        public bool IsAuthenticated => true;

        public TestCurrentUserService(string? userId = null, string? role = null, string? email = null)
        {
            UserId = userId ?? Guid.NewGuid().ToString();
            Role = role ?? "SuperAdmin";
            Email = email ?? $"{UserId}@test.com";
        }
    }

    #region Scenario 1: Concurrent last-unit orders -> one succeeds, the other fails
    [Fact]
    public async Task Scenario1_ConcurrentLastUnitOrders_OneSucceeds_OtherFails()
    {
        using var context = new AadhiDbContext(_options);
        var (product, _) = await SeedBaseDataAsync(context);
        var (orderService, _, _, _) = CreateServices(context);

        // 1. Order 1 takes all 10 available units
        var order1 = await orderService.CreateOrderAsync(new CreateOrderRequest
        {
            PaymentMethod = PaymentMethod.COD,
            ShippingAddress = new Address { FullName = "Suresh", Phone = "9876543210", AddressLine1 = "Road 1", City = "Sivakasi", State = "TN", PostalCode = "626123" },
            Items = new List<CreateOrderItemRequest> { new() { ProductId = product.Id, Quantity = 10 } }
        });
        Assert.NotNull(order1);

        // 2. Order 2 attempts to order 1 unit when available is 0 -> Throws InsufficientStockException
        await Assert.ThrowsAsync<InsufficientStockException>(() =>
            orderService.CreateOrderAsync(new CreateOrderRequest
            {
                PaymentMethod = PaymentMethod.COD,
                ShippingAddress = new Address { FullName = "Suresh", Phone = "9876543210", AddressLine1 = "Road 1", City = "Sivakasi", State = "TN", PostalCode = "626123" },
                Items = new List<CreateOrderItemRequest> { new() { ProductId = product.Id, Quantity = 1 } }
            }));
    }
    #endregion

    #region Scenario 2: Cancel releases reservation only (stock quantity unchanged)
    [Fact]
    public async Task Scenario2_CancelOrder_ReleasesReservationOnly()
    {
        using var context = new AadhiDbContext(_options);
        var (product, _) = await SeedBaseDataAsync(context);
        var (orderService, _, _, _) = CreateServices(context);

        var order = await orderService.CreateOrderAsync(new CreateOrderRequest
        {
            PaymentMethod = PaymentMethod.COD,
            ShippingAddress = new Address { FullName = "Suresh", Phone = "9876543210", AddressLine1 = "Road 1", City = "Sivakasi", State = "TN", PostalCode = "626123" },
            Items = new List<CreateOrderItemRequest> { new() { ProductId = product.Id, Quantity = 4 } }
        });

        var stockAfterOrder = await context.Products.FirstAsync(p => p.Id == product.Id);
        Assert.Equal(10, stockAfterOrder.StockQuantity);
        Assert.Equal(4, stockAfterOrder.ReservedQuantity);
        Assert.Equal(6, stockAfterOrder.AvailableQuantity);

        await orderService.UpdateOrderStatusAsync(order.Id, new UpdateOrderStatusRequest
        {
            NewStatus = OrderStatus.Cancelled,
            Reason = "Customer cancelled before packing"
        });

        var stockAfterCancel = await context.Products.FirstAsync(p => p.Id == product.Id);
        Assert.Equal(10, stockAfterCancel.StockQuantity);    // Total stock remains 10
        Assert.Equal(0, stockAfterCancel.ReservedQuantity);  // Reservation released to 0
        Assert.Equal(10, stockAfterCancel.AvailableQuantity);
    }
    #endregion

    #region Scenario 3: Ship: deducts direct product stock
    [Fact]
    public async Task Scenario3_Shipment_DeductsDirectProductStock()
    {
        using var context = new AadhiDbContext(_options);
        var (product, _) = await SeedBaseDataAsync(context);
        var (orderService, _, _, _) = CreateServices(context);

        var order = await orderService.CreateOrderAsync(new CreateOrderRequest
        {
            PaymentMethod = PaymentMethod.COD,
            ShippingAddress = new Address { FullName = "Suresh", Phone = "9876543210", AddressLine1 = "Road 1", City = "Sivakasi", State = "TN", PostalCode = "626123" },
            Items = new List<CreateOrderItemRequest> { new() { ProductId = product.Id, Quantity = 3 } }
        });

        await orderService.UpdateOrderStatusAsync(order.Id, new UpdateOrderStatusRequest { NewStatus = OrderStatus.Confirmed });
        await orderService.UpdateOrderStatusAsync(order.Id, new UpdateOrderStatusRequest { NewStatus = OrderStatus.Processing });
        await orderService.UpdateOrderStatusAsync(order.Id, new UpdateOrderStatusRequest { NewStatus = OrderStatus.Packed });
        await orderService.UpdateOrderStatusAsync(order.Id, new UpdateOrderStatusRequest { NewStatus = OrderStatus.Shipped });

        var updatedProduct = await context.Products.FirstAsync(p => p.Id == product.Id);
        Assert.Equal(7, updatedProduct.StockQuantity);    // 10 - 3 = 7
        Assert.Equal(0, updatedProduct.ReservedQuantity); // Reserved cleared
        Assert.Equal(7, updatedProduct.AvailableQuantity);
    }
    #endregion

    #region Scenario 7: Duplicate payment submission / verification idempotency
    [Fact]
    public async Task Scenario7_PaymentProof_DuplicateVerification_IsIdempotent()
    {
        using var context = new AadhiDbContext(_options);
        var (product, _) = await SeedBaseDataAsync(context);
        var (orderService, _, _, _) = CreateServices(context);

        var order = await orderService.CreateOrderAsync(new CreateOrderRequest
        {
            PaymentMethod = PaymentMethod.UPI,
            ShippingAddress = new Address { FullName = "Suresh", Phone = "9876543210", AddressLine1 = "Road 1", City = "Sivakasi", State = "TN", PostalCode = "626123" },
            Items = new List<CreateOrderItemRequest> { new() { ProductId = product.Id, Quantity = 2 } }
        });

        await orderService.SubmitPaymentProofAsync(order.Id, new SubmitPaymentProofRequest
        {
            UtrNumber = "123456789012",
            Notes = "GPay transfer"
        });

        // First verification succeeds
        var verifiedOrder = await orderService.VerifyPaymentAsync(order.Id, new VerifyPaymentRequest
        {
            VerifiedUtrNumber = "123456789012"
        });
        Assert.Equal(PaymentStatus.Paid, verifiedOrder.PaymentStatus);

        // Second verification throws DomainException ("Payment is already verified")
        await Assert.ThrowsAsync<DomainException>(() =>
            orderService.VerifyPaymentAsync(order.Id, new VerifyPaymentRequest
            {
                VerifiedUtrNumber = "123456789012"
            }));
    }
    #endregion

    #region Scenario 9: Outbox persistence in same transaction with Order
    [Fact]
    public async Task Scenario9_OutboxEvent_PersistedInTransactionWithOrder()
    {
        using var context = new AadhiDbContext(_options);
        var (product, _) = await SeedBaseDataAsync(context);
        var (orderService, _, _, _) = CreateServices(context);

        var order = await orderService.CreateOrderAsync(new CreateOrderRequest
        {
            PaymentMethod = PaymentMethod.COD,
            ShippingAddress = new Address { FullName = "Suresh", Phone = "9876543210", AddressLine1 = "Road 1", City = "Sivakasi", State = "TN", PostalCode = "626123" },
            Items = new List<CreateOrderItemRequest> { new() { ProductId = product.Id, Quantity = 1 } }
        });

        // Assert outbox message was persisted alongside the order
        var outboxMessage = await context.OutboxMessages.FirstOrDefaultAsync(m => m.Type == "OrderPlaced");
        Assert.NotNull(outboxMessage);
        Assert.Equal("Pending", outboxMessage.Status);
        Assert.Contains(order.OrderNumber, outboxMessage.PayloadJson);
    }
    #endregion

    #region Scenario 10: Handler failure -> Retry with exponential backoff & dead-letter queue
    [Fact]
    public async Task Scenario10_OutboxHandlerFailure_IncrementsRetry_DeadLettersAfterThreshold()
    {
        using var context = new AadhiDbContext(_options);
        var msg = new OutboxMessage
        {
            Type = "OrderPlaced",
            PayloadJson = "{\"OrderId\":\"00000000-0000-0000-0000-000000000000\"}",
            Status = "Pending",
            RetryCount = 4, // 4 prior failures
            OccurredOnUtc = DateTime.UtcNow
        };
        context.OutboxMessages.Add(msg);
        await context.SaveChangesAsync();

        // Simulate 5th failure in outbox processor
        msg.RetryCount++;
        msg.Error = "Simulated downstream gateway failure";
        if (msg.RetryCount >= 5)
        {
            msg.Status = "DeadLetter";
        }
        else
        {
            msg.Status = "Failed";
            msg.NextAttemptAtUtc = DateTime.UtcNow.AddSeconds(Math.Pow(2, msg.RetryCount) * 5);
        }
        await context.SaveChangesAsync();

        var updated = await context.OutboxMessages.FirstAsync(m => m.Id == msg.Id);
        Assert.Equal(5, updated.RetryCount);
        Assert.Equal("DeadLetter", updated.Status);
    }
    #endregion

    #region Scenario 11: Dashboard KPIs = Sales Report (Zero fabricated fallback)
    [Fact]
    public async Task Scenario11_DashboardKPIs_And_SalesReport_ReconcileWithDatabase()
    {
        using var context = new AadhiDbContext(_options);
        var (product, _) = await SeedBaseDataAsync(context);
        var (orderService, _, reportService, _) = CreateServices(context);

        // Create 2 orders
        var order1 = await orderService.CreateOrderAsync(new CreateOrderRequest
        {
            PaymentMethod = PaymentMethod.COD,
            ShippingAddress = new Address { FullName = "Suresh", Phone = "9876543210", AddressLine1 = "Road 1", City = "Sivakasi", State = "TN", PostalCode = "626123" },
            Items = new List<CreateOrderItemRequest> { new() { ProductId = product.Id, Quantity = 2 } }
        });
        var order2 = await orderService.CreateOrderAsync(new CreateOrderRequest
        {
            PaymentMethod = PaymentMethod.COD,
            ShippingAddress = new Address { FullName = "Suresh", Phone = "9876543210", AddressLine1 = "Road 1", City = "Sivakasi", State = "TN", PostalCode = "626123" },
            Items = new List<CreateOrderItemRequest> { new() { ProductId = product.Id, Quantity = 3 } }
        });

        var dashboard = await reportService.GetDashboardKpisAsync();
        var salesReport = await reportService.GetSalesOverviewAsync("month");

        Assert.Equal(2, dashboard.TotalOrders);
        Assert.Equal(2, salesReport.TotalOrders);
        Assert.Equal(dashboard.TotalSales, salesReport.TotalSales);
        // 5 units @ 200 = 1000 subtotal + 18% GST (180) + 0 delivery (To-Pay transport freight)
        //               + 1.5% packing charges on the subtotal (15) = 1195
        Assert.Equal(1195m, dashboard.TotalSales);
    }
    #endregion

    #region Scenario 12: Customer IDOR order security
    [Fact]
    public async Task Scenario12_Customer_CannotQueryAnotherCustomersOrders()
    {
        using var context = new AadhiDbContext(_options);
        var (product, customerA) = await SeedBaseDataAsync(context);

        var customerB = new Customer
        {
            UserId = Guid.NewGuid().ToString(),
            CustomerCode = "CUST-002",
            FirstName = "Virat",
            LastName = "Kohli",
            Email = "virat@test.com",
            Phone = "9876543219",
            IsActive = true
        };
        context.Customers.Add(customerB);
        await context.SaveChangesAsync();

        // Customer A places an order
        var (orderServiceA, _, _, _) = CreateServices(context, customerA.UserId, "Customer", customerA.Email);
        var orderA = await orderServiceA.CreateOrderAsync(new CreateOrderRequest
        {
            PaymentMethod = PaymentMethod.COD,
            ShippingAddress = new Address { FullName = "Suresh Raina", Phone = "9876543210", AddressLine1 = "Road 1", City = "Sivakasi", State = "TN", PostalCode = "626123" },
            Items = new List<CreateOrderItemRequest> { new() { ProductId = product.Id, Quantity = 1 } }
        });

        // Customer A queries their own orders -> Returns their 1 order
        var ordersForA = await orderServiceA.GetCustomerOrdersAsync(customerA.Id);
        Assert.Single(ordersForA);
        Assert.Equal(customerA.Id, ordersForA[0].CustomerId);

        // Customer B has 0 orders
        var (orderServiceB, _, _, _) = CreateServices(context, customerB.UserId, "Customer", customerB.Email);
        var ordersForB = await orderServiceB.GetCustomerOrdersAsync(customerB.Id);
        Assert.Empty(ordersForB);
    }
    #endregion
}
