using System.Security.Claims;
using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Application.Services;
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

public class ReturnsRefundsAndFinanceWorkflowTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AadhiDbContext> _options;

    public ReturnsRefundsAndFinanceWorkflowTests()
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
        public string? UserId => Guid.NewGuid().ToString();
        public string? Email => "test.user@aadhicrackers.com";
        public string? UserName => "testuser";
        public string? Role => "Admin";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "xUnit-Test-Runner";
        public string CorrelationId => Guid.NewGuid().ToString();
        public bool IsAuthenticated => true;
    }

    private static (OrderService orderService, FinanceService financeService) CreateServices(AadhiDbContext context)
    {
        var user = new TestCurrentUserService();
        var outbox = new OutboxService(context);
        var audit = new AuditLogService(context, user, outbox);
        var numberGen = new BusinessNumberGenerator(context);
        var orderService = new OrderService(context, user, audit, outbox, numberGen);
        var financeService = new FinanceService(context, user, audit, outbox, numberGen);
        return (orderService, financeService);
    }

    private static async Task<(Product product, Warehouse warehouse, Customer customer)> SeedBaseDataAsync(AadhiDbContext context)
    {
        var category = new Category { Name = "Crackers", Slug = "crackers", IsActive = true };
        context.Categories.Add(category);

        var product = new Product
        {
            Name = "Flower Pots Special",
            SKU = "FLW-001",
            Slug = "flower-pots-special",
            CategoryId = category.Id,
            Price = Money.FromDecimal(200m),
            TaxRate = 18m,
            StockQuantity = 100,
            ReservedQuantity = 0,
            IsActive = true
        };
        context.Products.Add(product);

        var warehouse = new Warehouse
        {
            Code = "WH-MAIN",
            Name = "Main Warehouse",
            Phone = "9876543210",
            IsActive = true,
            IsPrimary = true
        };
        context.Warehouses.Add(warehouse);

        var stockItem = new StockItem
        {
            ProductId = product.Id,
            WarehouseId = warehouse.Id,
            QuantityOnHand = 100,
            QuantityReserved = 0,
            ReorderLevel = 10
        };
        context.StockItems.Add(stockItem);

        var customer = new Customer
        {
            UserId = Guid.NewGuid().ToString(),
            CustomerCode = "CUST-001",
            FirstName = "Ravi",
            LastName = "Kumar",
            Email = "test.user@aadhicrackers.com",
            Phone = "9876543210",
            IsActive = true
        };
        context.Customers.Add(customer);

        await context.SaveChangesAsync();
        return (product, warehouse, customer);
    }

    [Fact]
    public async Task BusinessNumberGenerator_ShouldGenerateSequentialCollisionFreeNumbers()
    {
        using var context = new AadhiDbContext(_options);
        var gen = new BusinessNumberGenerator(context);

        var ord1 = await gen.GenerateOrderNumberAsync();
        var ord2 = await gen.GenerateOrderNumberAsync();
        var inv1 = await gen.GenerateInvoiceNumberAsync();
        var pay1 = await gen.GeneratePaymentNumberAsync();
        var ref1 = await gen.GenerateRefundNumberAsync();
        var ret1 = await gen.GenerateReturnNumberAsync();

        var year = DateTime.UtcNow.Year;
        Assert.Equal($"ORD-{year}-000001", ord1);
        Assert.Equal($"INV-{year}-000001", inv1);
        Assert.Equal($"PAY-{year}-000001", pay1);
        Assert.Equal($"REF-{year}-000001", ref1);
        Assert.Equal($"RET-{year}-000001", ret1);
    }

    [Fact]
    public async Task CompleteReturnWorkflow_SellableRestocked_DamagedWrittenOff()
    {
        using var context = new AadhiDbContext(_options);
        var (product, warehouse, customer) = await SeedBaseDataAsync(context);
        var (orderService, financeService) = CreateServices(context);

        // 1. Create order for 10 units
        var order = await orderService.CreateOrderAsync(new CreateOrderRequest
        {
            WarehouseId = warehouse.Id,
            PaymentMethod = PaymentMethod.UPI,
            ShippingAddress = new Address
            {
                FullName = "Ravi Kumar",
                Phone = "9876543210",
                AddressLine1 = "12 Main St",
                City = "Sivakasi",
                State = "Tamil Nadu",
                PostalCode = "626123"
            },
            Items = new List<CreateOrderItemRequest>
            {
                new() { ProductId = product.Id, Quantity = 10 }
            }
        });

        // 2. Deliver order (Sale deducted on shipment, final status Delivered)
        await orderService.UpdateOrderStatusAsync(order.Id, new UpdateOrderStatusRequest { NewStatus = OrderStatus.Confirmed });
        await orderService.UpdateOrderStatusAsync(order.Id, new UpdateOrderStatusRequest { NewStatus = OrderStatus.Processing });
        await orderService.UpdateOrderStatusAsync(order.Id, new UpdateOrderStatusRequest { NewStatus = OrderStatus.Packed });
        await orderService.UpdateOrderStatusAsync(order.Id, new UpdateOrderStatusRequest { NewStatus = OrderStatus.Shipped });
        await orderService.UpdateOrderStatusAsync(order.Id, new UpdateOrderStatusRequest { NewStatus = OrderStatus.Delivered });

        var stockAfterDelivery = await context.StockItems.FirstAsync(s => s.ProductId == product.Id);
        Assert.Equal(90, stockAfterDelivery.QuantityOnHand); // 100 - 10 = 90

        // 3. Customer requests return of 4 units (2 sellable, 2 damaged)
        var returnOrder = await orderService.CreateReturnOrderAsync(new CreateReturnOrderRequest
        {
            OrderId = order.Id,
            Reason = "Defective packaging on 2 items, 2 unopened",
            Items = new List<CreateReturnOrderItemRequest>
            {
                new() { ProductId = product.Id, Quantity = 4, Reason = "Mixed condition" }
            }
        });

        Assert.Equal("Requested", returnOrder.Status);
        Assert.StartsWith($"RET-{DateTime.UtcNow.Year}-", returnOrder.ReturnNumber);

        // 4. Admin approves return
        var approved = await orderService.ApproveReturnOrderAsync(returnOrder.Id, "Return approved for inspection");
        Assert.Equal("Approved", approved.Status);

        // 5. Warehouse receives return package
        var received = await orderService.ReceiveReturnOrderAsync(returnOrder.Id, "Package arrived at Sivakasi WH");
        Assert.Equal("Received", received.Status);

        // 6. Inspection: 2 sellable, 2 damaged
        var inspected = await orderService.InspectReturnOrderAsync(returnOrder.Id, new InspectReturnOrderRequest
        {
            InspectionNotes = "2 items in factory condition, 2 items water damaged",
            ItemInspections = new List<InspectReturnItemRequest>
            {
                new() { ProductId = product.Id, Quantity = 2, IsSellable = true, ConditionNotes = "Factory seal intact" },
                new() { ProductId = product.Id, Quantity = 2, IsSellable = false, ConditionNotes = "Water damaged carton" }
            }
        });

        Assert.Equal("Inspected", inspected.Status);
        Assert.True(inspected.IsSellable);
        Assert.Equal(400m, inspected.RefundAmount); // 2 sellable * ₹200 = ₹400

        // Verify stock ledger math: ONLY sellable items (2 units) increment QuantityOnHand (90 -> 92)
        var stockAfterInspection = await context.StockItems.FirstAsync(s => s.ProductId == product.Id);
        var productAfterInspection = await context.Products.FirstAsync(p => p.Id == product.Id);
        Assert.Equal(92, stockAfterInspection.QuantityOnHand);
        Assert.Equal(92, productAfterInspection.StockQuantity);

        // Verify StockMovements ledger has Return (+2) and Damage (0) records
        var movements = await context.StockMovements
            .Where(m => m.ProductId == product.Id)
            .OrderBy(m => m.CreatedAtUtc)
            .ToListAsync();

        var returnMovement = movements.First(m => m.MovementType == StockMovementType.Return);
        Assert.Equal(90, returnMovement.QuantityBefore);
        Assert.Equal(2, returnMovement.QuantityChange);
        Assert.Equal(92, returnMovement.QuantityAfter);

        var damageMovement = movements.First(m => m.MovementType == StockMovementType.Damage);
        Assert.Equal(92, damageMovement.QuantityBefore);
        Assert.Equal(0, damageMovement.QuantityChange);
        Assert.Equal(92, damageMovement.QuantityAfter);
    }

    [Fact]
    public async Task PaymentVerificationAndUtrDuplicateGuard_ShouldEnforceRules()
    {
        using var context = new AadhiDbContext(_options);
        var (product, warehouse, customer) = await SeedBaseDataAsync(context);
        var (orderService, financeService) = CreateServices(context);

        var order = await orderService.CreateOrderAsync(new CreateOrderRequest
        {
            WarehouseId = warehouse.Id,
            PaymentMethod = PaymentMethod.UPI,
            ShippingAddress = new Address { FullName = "Ravi Kumar", Phone = "9876543210", AddressLine1 = "12 Main St", City = "Sivakasi", State = "Tamil Nadu", PostalCode = "626123" },
            Items = new List<CreateOrderItemRequest> { new() { ProductId = product.Id, Quantity = 2 } }
        });

        // 1. Submit payment proof
        await orderService.SubmitPaymentProofAsync(order.Id, new SubmitPaymentProofRequest
        {
            UtrNumber = "UTR-AADHI-99887766",
            PaymentScreenshotUrl = "https://cdn.aadhicrackers.com/proofs/pay1.png",
            Notes = "GPay payment completed"
        });

        var updatedOrder = await orderService.GetOrderByIdAsync(order.Id);
        Assert.Equal("UTR-AADHI-99887766", updatedOrder!.UtrNumber);

        // 2. Admin verifies payment
        var verifiedOrder = await orderService.VerifyPaymentAsync(order.Id, new VerifyPaymentRequest
        {
            VerifiedUtrNumber = "UTR-AADHI-99887766",
            VerificationNotes = "Bank statement confirmed"
        });

        Assert.Equal(OrderStatus.Confirmed, verifiedOrder.OrderStatus);
        Assert.Equal(PaymentStatus.Paid, verifiedOrder.PaymentStatus);

        // 3. Record payment directly in FinanceService using duplicate UTR -> Should throw DomainException
        await Assert.ThrowsAsync<DomainException>(() => financeService.CreatePaymentAsync(new CreatePaymentRequest
        {
            OrderId = order.Id,
            CustomerId = customer.Id,
            Amount = 100m,
            PaymentMethod = PaymentMethod.UPI,
            UtrNumber = "UTR-AADHI-99887766" // Duplicate UTR
        }));
    }

    [Fact]
    public async Task RefundProcessing_ShouldEnforceRefundableBalanceAndIdempotency()
    {
        using var context = new AadhiDbContext(_options);
        var (product, warehouse, customer) = await SeedBaseDataAsync(context);
        var (orderService, financeService) = CreateServices(context);

        var order = await orderService.CreateOrderAsync(new CreateOrderRequest
        {
            WarehouseId = warehouse.Id,
            PaymentMethod = PaymentMethod.UPI,
            ShippingAddress = new Address { FullName = "Ravi Kumar", Phone = "9876543210", AddressLine1 = "12 Main St", City = "Sivakasi", State = "Tamil Nadu", PostalCode = "626123" },
            Items = new List<CreateOrderItemRequest> { new() { ProductId = product.Id, Quantity = 5 } }
        });

        // Pay ₹1000 for order
        var payment = await financeService.CreatePaymentAsync(new CreatePaymentRequest
        {
            OrderId = order.Id,
            CustomerId = customer.Id,
            Amount = 1000m,
            PaymentMethod = PaymentMethod.UPI,
            UtrNumber = "UTR-12345678",
            IdempotencyKey = "IDEMP-PAY-001"
        });

        Assert.Equal(1000m, payment.Amount);

        // 1. Process partial refund of ₹400
        var refund1 = await financeService.CreateRefundAsync(new CreateRefundRequest
        {
            OrderId = order.Id,
            PaymentId = payment.Id,
            Amount = 400m,
            Reason = "Return items accepted",
            IdempotencyKey = "IDEMP-REF-001"
        });

        Assert.Equal(400m, refund1.Amount);
        Assert.Equal("Completed", refund1.Status);
        Assert.StartsWith($"REF-{DateTime.UtcNow.Year}-", refund1.RefundNumber);

        // 2. Idempotent retry of same refund -> Returns identical refund record
        var idempotentRefund = await financeService.CreateRefundAsync(new CreateRefundRequest
        {
            OrderId = order.Id,
            PaymentId = payment.Id,
            Amount = 400m,
            Reason = "Return items accepted",
            IdempotencyKey = "IDEMP-REF-001"
        });
        Assert.Equal(refund1.Id, idempotentRefund.Id);

        // 3. Attempting to refund ₹700 when only ₹600 refundable remaining -> Throws DomainException
        await Assert.ThrowsAsync<DomainException>(() => financeService.CreateRefundAsync(new CreateRefundRequest
        {
            OrderId = order.Id,
            PaymentId = payment.Id,
            Amount = 700m,
            Reason = "Excess refund"
        }));

        // 4. Refund remaining ₹600 -> Should succeed and mark Order PaymentStatus = Refunded
        var refund2 = await financeService.CreateRefundAsync(new CreateRefundRequest
        {
            OrderId = order.Id,
            PaymentId = payment.Id,
            Amount = 600m,
            Reason = "Final settlement refund"
        });

        Assert.Equal(600m, refund2.Amount);

        var finalOrder = await orderService.GetOrderByIdAsync(order.Id);
        Assert.Equal(PaymentStatus.Refunded, finalOrder!.PaymentStatus);
    }
}
