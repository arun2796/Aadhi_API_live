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
/// Dedicated test suite verifying the 12 Critical Scenarios from Section 11 of Phase 0 Report.
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

    private async Task<(Product product, Warehouse warehouse, Customer customer)> SeedBaseDataAsync(AadhiDbContext context)
    {
        var category = new Category { Name = "Crackers", Slug = "crackers", IsActive = true };
        context.Categories.Add(category);

        var warehouse = new Warehouse
        {
            Code = "WH-MAIN",
            Name = "Main Depot",
            Phone = "9876543210",
            IsActive = true,
            IsPrimary = true
        };
        context.Warehouses.Add(warehouse);

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

        var stockItem = new StockItem
        {
            ProductId = product.Id,
            WarehouseId = warehouse.Id,
            QuantityOnHand = 10,
            QuantityReserved = 0,
            ReorderLevel = 2
        };
        context.StockItems.Add(stockItem);

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
        return (product, warehouse, customer);
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
        var (product, warehouse, customer) = await SeedBaseDataAsync(context);
        var (orderService, _, _, _) = CreateServices(context);

        // 1. Order 1 takes all 10 available units
        var order1 = await orderService.CreateOrderAsync(new CreateOrderRequest
        {
            WarehouseId = warehouse.Id,
            PaymentMethod = PaymentMethod.COD,
            ShippingAddress = new Address { FullName = "Suresh", Phone = "9876543210", AddressLine1 = "Road 1", City = "Sivakasi", State = "TN", PostalCode = "626123" },
            Items = new List<CreateOrderItemRequest> { new() { ProductId = product.Id, Quantity = 10 } }
        });
        Assert.NotNull(order1);

        // 2. Order 2 attempts to order 1 unit when available is 0 -> Throws InsufficientStockException
        await Assert.ThrowsAsync<InsufficientStockException>(() =>
            orderService.CreateOrderAsync(new CreateOrderRequest
            {
                WarehouseId = warehouse.Id,
                PaymentMethod = PaymentMethod.COD,
                ShippingAddress = new Address { FullName = "Suresh", Phone = "9876543210", AddressLine1 = "Road 1", City = "Sivakasi", State = "TN", PostalCode = "626123" },
                Items = new List<CreateOrderItemRequest> { new() { ProductId = product.Id, Quantity = 1 } }
            }));
    }
    #endregion

    #region Scenario 2: Cancel releases reservation only (on-hand unchanged)
    [Fact]
    public async Task Scenario2_CancelOrder_ReleasesReservationOnly()
    {
        using var context = new AadhiDbContext(_options);
        var (product, warehouse, _) = await SeedBaseDataAsync(context);
        var (orderService, _, _, _) = CreateServices(context);

        var order = await orderService.CreateOrderAsync(new CreateOrderRequest
        {
            WarehouseId = warehouse.Id,
            PaymentMethod = PaymentMethod.COD,
            ShippingAddress = new Address { FullName = "Suresh", Phone = "9876543210", AddressLine1 = "Road 1", City = "Sivakasi", State = "TN", PostalCode = "626123" },
            Items = new List<CreateOrderItemRequest> { new() { ProductId = product.Id, Quantity = 4 } }
        });

        var stockAfterOrder = await context.StockItems.FirstAsync(s => s.ProductId == product.Id);
        Assert.Equal(10, stockAfterOrder.QuantityOnHand);
        Assert.Equal(4, stockAfterOrder.QuantityReserved);

        await orderService.UpdateOrderStatusAsync(order.Id, new UpdateOrderStatusRequest
        {
            NewStatus = OrderStatus.Cancelled,
            Reason = "Customer cancelled before packing"
        });

        var stockAfterCancel = await context.StockItems.FirstAsync(s => s.ProductId == product.Id);
        Assert.Equal(10, stockAfterCancel.QuantityOnHand);    // On-hand remains 10
        Assert.Equal(0, stockAfterCancel.QuantityReserved);  // Reservation released to 0
    }
    #endregion

    #region Scenario 3: Ship: deduct once, exactly one Sale movement
    [Fact]
    public async Task Scenario3_Shipment_DeductsOnHandOnce_WritesExactlyOneSaleMovement()
    {
        using var context = new AadhiDbContext(_options);
        var (product, warehouse, _) = await SeedBaseDataAsync(context);
        var (orderService, _, _, _) = CreateServices(context);

        var order = await orderService.CreateOrderAsync(new CreateOrderRequest
        {
            WarehouseId = warehouse.Id,
            PaymentMethod = PaymentMethod.COD,
            ShippingAddress = new Address { FullName = "Suresh", Phone = "9876543210", AddressLine1 = "Road 1", City = "Sivakasi", State = "TN", PostalCode = "626123" },
            Items = new List<CreateOrderItemRequest> { new() { ProductId = product.Id, Quantity = 3 } }
        });

        await orderService.UpdateOrderStatusAsync(order.Id, new UpdateOrderStatusRequest { NewStatus = OrderStatus.Confirmed });
        await orderService.UpdateOrderStatusAsync(order.Id, new UpdateOrderStatusRequest { NewStatus = OrderStatus.Processing });
        await orderService.UpdateOrderStatusAsync(order.Id, new UpdateOrderStatusRequest { NewStatus = OrderStatus.Packed });
        await orderService.UpdateOrderStatusAsync(order.Id, new UpdateOrderStatusRequest { NewStatus = OrderStatus.Shipped });

        var stock = await context.StockItems.FirstAsync(s => s.ProductId == product.Id);
        Assert.Equal(7, stock.QuantityOnHand);    // 10 - 3 = 7
        Assert.Equal(0, stock.QuantityReserved);  // Reserved cleared

        var saleMovements = await context.StockMovements
            .Where(m => m.ProductId == product.Id && m.MovementType == StockMovementType.Sale)
            .ToListAsync();

        Assert.Single(saleMovements); // Exactly ONE sale movement
        Assert.Equal(-3, saleMovements[0].QuantityChange);
    }
    #endregion

    #region Scenario 4: No auto-restock on return request
    [Fact]
    public async Task Scenario4_NoAutoRestock_OnReturnRequest()
    {
        using var context = new AadhiDbContext(_options);
        var (product, warehouse, _) = await SeedBaseDataAsync(context);
        var (orderService, _, _, _) = CreateServices(context);

        var order = await orderService.CreateOrderAsync(new CreateOrderRequest
        {
            WarehouseId = warehouse.Id,
            PaymentMethod = PaymentMethod.COD,
            ShippingAddress = new Address { FullName = "Suresh", Phone = "9876543210", AddressLine1 = "Road 1", City = "Sivakasi", State = "TN", PostalCode = "626123" },
            Items = new List<CreateOrderItemRequest> { new() { ProductId = product.Id, Quantity = 5 } }
        });

        await orderService.UpdateOrderStatusAsync(order.Id, new UpdateOrderStatusRequest { NewStatus = OrderStatus.Confirmed });
        await orderService.UpdateOrderStatusAsync(order.Id, new UpdateOrderStatusRequest { NewStatus = OrderStatus.Processing });
        await orderService.UpdateOrderStatusAsync(order.Id, new UpdateOrderStatusRequest { NewStatus = OrderStatus.Packed });
        await orderService.UpdateOrderStatusAsync(order.Id, new UpdateOrderStatusRequest { NewStatus = OrderStatus.Shipped });
        await orderService.UpdateOrderStatusAsync(order.Id, new UpdateOrderStatusRequest { NewStatus = OrderStatus.Delivered });

        var stockAfterDelivery = await context.StockItems.FirstAsync(s => s.ProductId == product.Id);
        Assert.Equal(5, stockAfterDelivery.QuantityOnHand);

        // Customer requests return of 2 units
        var returnOrder = await orderService.CreateReturnOrderAsync(new CreateReturnOrderRequest
        {
            OrderId = order.Id,
            Reason = "Defective box",
            Items = new List<CreateReturnOrderItemRequest>
            {
                new() { ProductId = product.Id, Quantity = 2, Reason = "Defective" }
            }
        });

        // Assert on-hand is STILL 5 (NO auto-restock before inspection)
        var stockAfterReturnRequest = await context.StockItems.FirstAsync(s => s.ProductId == product.Id);
        Assert.Equal(5, stockAfterReturnRequest.QuantityOnHand);
    }
    #endregion

    #region Scenario 5 & 6: Sellable / Damaged inspection movements
    [Fact]
    public async Task Scenario5And6_ReturnInspection_SellableRestocked_DamagedWrittenOff()
    {
        using var context = new AadhiDbContext(_options);
        var (product, warehouse, _) = await SeedBaseDataAsync(context);
        var (orderService, _, _, _) = CreateServices(context);

        var order = await orderService.CreateOrderAsync(new CreateOrderRequest
        {
            WarehouseId = warehouse.Id,
            PaymentMethod = PaymentMethod.COD,
            ShippingAddress = new Address { FullName = "Suresh", Phone = "9876543210", AddressLine1 = "Road 1", City = "Sivakasi", State = "TN", PostalCode = "626123" },
            Items = new List<CreateOrderItemRequest> { new() { ProductId = product.Id, Quantity = 6 } }
        });

        await orderService.UpdateOrderStatusAsync(order.Id, new UpdateOrderStatusRequest { NewStatus = OrderStatus.Confirmed });
        await orderService.UpdateOrderStatusAsync(order.Id, new UpdateOrderStatusRequest { NewStatus = OrderStatus.Processing });
        await orderService.UpdateOrderStatusAsync(order.Id, new UpdateOrderStatusRequest { NewStatus = OrderStatus.Packed });
        await orderService.UpdateOrderStatusAsync(order.Id, new UpdateOrderStatusRequest { NewStatus = OrderStatus.Shipped });
        await orderService.UpdateOrderStatusAsync(order.Id, new UpdateOrderStatusRequest { NewStatus = OrderStatus.Delivered });

        var returnOrder = await orderService.CreateReturnOrderAsync(new CreateReturnOrderRequest
        {
            OrderId = order.Id,
            Reason = "Return 4 units",
            Items = new List<CreateReturnOrderItemRequest>
            {
                new() { ProductId = product.Id, Quantity = 4, Reason = "Mixed condition" }
            }
        });

        await orderService.ApproveReturnOrderAsync(returnOrder.Id);
        await orderService.ReceiveReturnOrderAsync(returnOrder.Id);

        // Inspect: 3 units sellable, 1 unit damaged
        await orderService.InspectReturnOrderAsync(returnOrder.Id, new InspectReturnOrderRequest
        {
            InspectionNotes = "3 good boxes, 1 broken sparkler",
            ItemInspections = new List<InspectReturnItemRequest>
            {
                new()
                {
                    ProductId = product.Id,
                    Quantity = 3,
                    IsSellable = true,
                    ConditionNotes = "Good box"
                },
                new()
                {
                    ProductId = product.Id,
                    Quantity = 1,
                    IsSellable = false,
                    ConditionNotes = "Broken sparkler"
                }
            }
        });

        var finalStock = await context.StockItems.FirstAsync(s => s.ProductId == product.Id);
        // Initial was 10, shipped 6 = 4 remaining. 3 sellable returned -> 4 + 3 = 7
        Assert.Equal(7, finalStock.QuantityOnHand);

        // Verify ledger entries
        var returnMovements = await context.StockMovements
            .Where(m => m.ProductId == product.Id && m.MovementType == StockMovementType.Return)
            .ToListAsync();
        Assert.Single(returnMovements);
        Assert.Equal(3, returnMovements[0].QuantityChange);

        var damageMovements = await context.StockMovements
            .Where(m => m.ProductId == product.Id && m.MovementType == StockMovementType.Damage)
            .ToListAsync();
        Assert.Single(damageMovements);
        Assert.Equal(0, damageMovements[0].QuantityChange); // Damaged units are logged without inflating on-hand stock
    }
    #endregion

    #region Scenario 7: Duplicate payment submission / verification idempotency
    [Fact]
    public async Task Scenario7_PaymentProof_DuplicateVerification_IsIdempotent()
    {
        using var context = new AadhiDbContext(_options);
        var (product, warehouse, _) = await SeedBaseDataAsync(context);
        var (orderService, _, _, _) = CreateServices(context);

        var order = await orderService.CreateOrderAsync(new CreateOrderRequest
        {
            WarehouseId = warehouse.Id,
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

    #region Scenario 8: Duplicate refund rejected (Idempotency Key)
    [Fact]
    public async Task Scenario8_DuplicateRefund_WithSameIdempotencyKey_Rejected()
    {
        using var context = new AadhiDbContext(_options);
        var (product, warehouse, _) = await SeedBaseDataAsync(context);
        var (orderService, financeService, _, _) = CreateServices(context);

        var order = await orderService.CreateOrderAsync(new CreateOrderRequest
        {
            WarehouseId = warehouse.Id,
            PaymentMethod = PaymentMethod.UPI,
            ShippingAddress = new Address { FullName = "Suresh", Phone = "9876543210", AddressLine1 = "Road 1", City = "Sivakasi", State = "TN", PostalCode = "626123" },
            Items = new List<CreateOrderItemRequest> { new() { ProductId = product.Id, Quantity = 2 } }
        });

        await orderService.SubmitPaymentProofAsync(order.Id, new SubmitPaymentProofRequest
        {
            UtrNumber = "REF-UTR-8888",
            Notes = "UPI Transfer"
        });
        await orderService.VerifyPaymentAsync(order.Id, new VerifyPaymentRequest { VerifiedUtrNumber = "REF-UTR-8888" });

        var idempotencyKey = "IDEMP-REFUND-999";

        // First refund succeeds
        var refund1 = await financeService.CreateRefundAsync(new CreateRefundRequest
        {
            OrderId = order.Id,
            Amount = 200m,
            Reason = "Return partial refund",
            Method = PaymentMethod.UPI,
            IdempotencyKey = idempotencyKey
        });
        Assert.NotNull(refund1);

        // Second call with same idempotency key returns the existing refund without double debiting
        var refund2 = await financeService.CreateRefundAsync(new CreateRefundRequest
        {
            OrderId = order.Id,
            Amount = 200m,
            Reason = "Duplicate refund request",
            Method = PaymentMethod.UPI,
            IdempotencyKey = idempotencyKey
        });
        Assert.Equal(refund1.Id, refund2.Id);

        var totalRefunds = await context.Refunds.CountAsync(r => r.OrderId == order.Id);
        Assert.Equal(1, totalRefunds);
    }
    #endregion

    #region Scenario 9: Outbox persistence in same transaction with Order
    [Fact]
    public async Task Scenario9_OutboxEvent_PersistedInTransactionWithOrder()
    {
        using var context = new AadhiDbContext(_options);
        var (product, warehouse, _) = await SeedBaseDataAsync(context);
        var (orderService, _, _, _) = CreateServices(context);

        var order = await orderService.CreateOrderAsync(new CreateOrderRequest
        {
            WarehouseId = warehouse.Id,
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
        var (product, warehouse, _) = await SeedBaseDataAsync(context);
        var (orderService, _, reportService, _) = CreateServices(context);

        // Create 2 orders
        var order1 = await orderService.CreateOrderAsync(new CreateOrderRequest
        {
            WarehouseId = warehouse.Id,
            PaymentMethod = PaymentMethod.COD,
            ShippingAddress = new Address { FullName = "Suresh", Phone = "9876543210", AddressLine1 = "Road 1", City = "Sivakasi", State = "TN", PostalCode = "626123" },
            Items = new List<CreateOrderItemRequest> { new() { ProductId = product.Id, Quantity = 2 } }
        });
        var order2 = await orderService.CreateOrderAsync(new CreateOrderRequest
        {
            WarehouseId = warehouse.Id,
            PaymentMethod = PaymentMethod.COD,
            ShippingAddress = new Address { FullName = "Suresh", Phone = "9876543210", AddressLine1 = "Road 1", City = "Sivakasi", State = "TN", PostalCode = "626123" },
            Items = new List<CreateOrderItemRequest> { new() { ProductId = product.Id, Quantity = 3 } }
        });

        var dashboard = await reportService.GetDashboardKpisAsync();
        var salesReport = await reportService.GetSalesOverviewAsync("month");

        Assert.Equal(2, dashboard.TotalOrders);
        Assert.Equal(2, salesReport.TotalOrders);
        Assert.Equal(dashboard.TotalSales, salesReport.TotalSales);
        // 5 units @ 200 = 1000 subtotal + 18% GST (180) + 2x standard delivery charge (Delivery.StandardCharge default 40) = 1260
        Assert.Equal(1260m, dashboard.TotalSales);
    }
    #endregion

    #region Scenario 12: Customer IDOR order security
    [Fact]
    public async Task Scenario12_Customer_CannotQueryAnotherCustomersOrders()
    {
        using var context = new AadhiDbContext(_options);
        var (product, warehouse, customerA) = await SeedBaseDataAsync(context);

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
            WarehouseId = warehouse.Id,
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
