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
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AadhiCrackers.Infrastructure.Tests;

public class OrderStockLifecycleAndConcurrencyTests
{
    [Fact]
    public async Task OrderCreation_ReservesStockInStockItem_AndLogsStockReservedMovement()
    {
        var dbPath = CreateTempDbPath();
        try
        {
            Guid orderId;
            Guid productId;
            Guid warehouseId;

            await using (var context = new AadhiDbContext(CreateSqliteOptions(dbPath)))
            {
                await DatabaseInitializer.InitializeAsync(context, NullLogger.Instance);
                var (category, product, warehouse, customer) = await SeedBaseDataAsync(context);
                productId = product.Id;
                warehouseId = warehouse.Id;

                var stockItem = new StockItem
                {
                    ProductId = product.Id,
                    WarehouseId = warehouse.Id,
                    QuantityOnHand = 10,
                    QuantityReserved = 0
                };
                context.StockItems.Add(stockItem);
                await context.SaveChangesAsync();

                var orderService = CreateOrderService(context);

                var request = new CreateOrderRequest
                {
                    WarehouseId = warehouse.Id,
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
                var updatedStock = await context.StockItems.FirstAsync(s => s.ProductId == productId && s.WarehouseId == warehouseId);
                updatedStock.QuantityOnHand.Should().Be(10);
                updatedStock.QuantityReserved.Should().Be(3);
                updatedStock.QuantityAvailable.Should().Be(7);

                var updatedProduct = await context.Products.FirstAsync(p => p.Id == productId);
                updatedProduct.StockQuantity.Should().Be(10);
                updatedProduct.ReservedQuantity.Should().Be(3);

                var movement = await context.StockMovements.FirstOrDefaultAsync(m => m.MovementType == StockMovementType.StockReserved);
                movement.Should().NotBeNull();
                movement!.QuantityBefore.Should().Be(10);
                movement.QuantityAfter.Should().Be(10);
                movement.QuantityChange.Should().Be(3);
            }
        }
        finally
        {
            DeleteDb(dbPath);
        }
    }

    [Fact]
    public async Task OrderCancellation_ReleasesReservedStock_AndLogsStockReservationReleased()
    {
        var dbPath = CreateTempDbPath();
        try
        {
            Guid orderId;
            Guid productId;
            Guid warehouseId;

            await using (var context = new AadhiDbContext(CreateSqliteOptions(dbPath)))
            {
                await DatabaseInitializer.InitializeAsync(context, NullLogger.Instance);
                var (category, product, warehouse, customer) = await SeedBaseDataAsync(context);
                productId = product.Id;
                warehouseId = warehouse.Id;

                var stockItem = new StockItem
                {
                    ProductId = product.Id,
                    WarehouseId = warehouse.Id,
                    QuantityOnHand = 10,
                    QuantityReserved = 0
                };
                context.StockItems.Add(stockItem);
                await context.SaveChangesAsync();

                var orderService = CreateOrderService(context);
                var order = await orderService.CreateOrderAsync(new CreateOrderRequest
                {
                    WarehouseId = warehouse.Id,
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
                var updatedStock = await context.StockItems.FirstAsync(s => s.ProductId == productId && s.WarehouseId == warehouseId);
                updatedStock.QuantityOnHand.Should().Be(10);
                updatedStock.QuantityReserved.Should().Be(0);
                updatedStock.QuantityAvailable.Should().Be(10);

                var releaseMovement = await context.StockMovements.FirstOrDefaultAsync(m => m.MovementType == StockMovementType.StockReservationReleased);
                releaseMovement.Should().NotBeNull();
                releaseMovement!.QuantityBefore.Should().Be(10);
                releaseMovement.QuantityAfter.Should().Be(10);
                releaseMovement.QuantityChange.Should().Be(4);
            }
        }
        finally
        {
            DeleteDb(dbPath);
        }
    }

    [Fact]
    public async Task OrderShipment_DeductsOnHandAndReserved_AndLogsExactlyOneSaleMovement()
    {
        var dbPath = CreateTempDbPath();
        try
        {
            Guid orderId;
            Guid productId;
            Guid warehouseId;

            await using (var context = new AadhiDbContext(CreateSqliteOptions(dbPath)))
            {
                await DatabaseInitializer.InitializeAsync(context, NullLogger.Instance);
                var (category, product, warehouse, customer) = await SeedBaseDataAsync(context);
                productId = product.Id;
                warehouseId = warehouse.Id;

                var stockItem = new StockItem
                {
                    ProductId = product.Id,
                    WarehouseId = warehouse.Id,
                    QuantityOnHand = 10,
                    QuantityReserved = 0
                };
                context.StockItems.Add(stockItem);
                await context.SaveChangesAsync();

                var orderService = CreateOrderService(context);
                var order = await orderService.CreateOrderAsync(new CreateOrderRequest
                {
                    WarehouseId = warehouse.Id,
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
                try
                {
                    await orderService.UpdateOrderStatusAsync(orderId, new UpdateOrderStatusRequest { NewStatus = status });
                }
                catch (DbUpdateConcurrencyException ex)
                {
                    var details = string.Join("; ", ex.Entries.Select(e => $"{e.Entity.GetType().Name} state={e.State}"));
                    throw new Exception($"Concurrency failure on status {status}: {details}", ex);
                }
            }

            // Verify Stock deduction
            await using (var context = new AadhiDbContext(CreateSqliteOptions(dbPath)))
            {
                var updatedStock = await context.StockItems.FirstAsync(s => s.ProductId == productId && s.WarehouseId == warehouseId);
                updatedStock.QuantityOnHand.Should().Be(7);
                updatedStock.QuantityReserved.Should().Be(0);
                updatedStock.QuantityAvailable.Should().Be(7);

                // Verify exactly one Sale movement
                var saleMovements = await context.StockMovements.Where(m => m.MovementType == StockMovementType.Sale).ToListAsync();
                saleMovements.Should().HaveCount(1);
                saleMovements[0].QuantityBefore.Should().Be(10);
                saleMovements[0].QuantityChange.Should().Be(-3);
                saleMovements[0].QuantityAfter.Should().Be(7);
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
                var finalStock = await context.StockItems.FirstAsync(s => s.ProductId == productId && s.WarehouseId == warehouseId);
                finalStock.QuantityOnHand.Should().Be(7);
                (await context.StockMovements.CountAsync(m => m.MovementType == StockMovementType.Sale)).Should().Be(1);
            }
        }
        finally
        {
            DeleteDb(dbPath);
        }
    }

    [Fact]
    public async Task ReturnedStatus_DoesNotAutoRestockWithoutInspection()
    {
        var dbPath = CreateTempDbPath();
        try
        {
            Guid orderId;
            Guid productId;
            Guid warehouseId;

            await using (var context = new AadhiDbContext(CreateSqliteOptions(dbPath)))
            {
                await DatabaseInitializer.InitializeAsync(context, NullLogger.Instance);
                var (category, product, warehouse, customer) = await SeedBaseDataAsync(context);
                productId = product.Id;
                warehouseId = warehouse.Id;

                var stockItem = new StockItem
                {
                    ProductId = product.Id,
                    WarehouseId = warehouse.Id,
                    QuantityOnHand = 10,
                    QuantityReserved = 0
                };
                context.StockItems.Add(stockItem);
                await context.SaveChangesAsync();

                var orderService = CreateOrderService(context);
                var order = await orderService.CreateOrderAsync(new CreateOrderRequest
                {
                    WarehouseId = warehouse.Id,
                    ShippingAddress = new Address("Arun Kumar", "9876543210", "123 Main St", null, "Sivakasi", "TN", "626123"),
                    Items = new List<CreateOrderItemRequest> { new() { ProductId = product.Id, Quantity = 2 } }
                });
                orderId = order.Id;
            }

            foreach (var status in new[] { OrderStatus.Confirmed, OrderStatus.Processing, OrderStatus.Packed, OrderStatus.Shipped, OrderStatus.OutForDelivery, OrderStatus.Delivered, OrderStatus.Returned })
            {
                await using var context = new AadhiDbContext(CreateSqliteOptions(dbPath));
                var orderService = CreateOrderService(context);
                await orderService.UpdateOrderStatusAsync(orderId, new UpdateOrderStatusRequest { NewStatus = status, Reason = "Status change" });
            }

            // Stock must NOT auto-restock upon return status alone
            await using (var context = new AadhiDbContext(CreateSqliteOptions(dbPath)))
            {
                var stock = await context.StockItems.FirstAsync(s => s.ProductId == productId && s.WarehouseId == warehouseId);
                stock.QuantityOnHand.Should().Be(8);
            }
        }
        finally
        {
            DeleteDb(dbPath);
        }
    }

    [Fact]
    public async Task PaymentRejection_ReleasesReservation_AndLogsMovementAndAudit()
    {
        var dbPath = CreateTempDbPath();
        try
        {
            Guid orderId;
            Guid productId;
            Guid warehouseId;

            await using (var context = new AadhiDbContext(CreateSqliteOptions(dbPath)))
            {
                await DatabaseInitializer.InitializeAsync(context, NullLogger.Instance);
                var (category, product, warehouse, customer) = await SeedBaseDataAsync(context);
                productId = product.Id;
                warehouseId = warehouse.Id;

                var stockItem = new StockItem
                {
                    ProductId = product.Id,
                    WarehouseId = warehouse.Id,
                    QuantityOnHand = 10,
                    QuantityReserved = 0
                };
                context.StockItems.Add(stockItem);
                await context.SaveChangesAsync();

                var orderService = CreateOrderService(context);
                var order = await orderService.CreateOrderAsync(new CreateOrderRequest
                {
                    WarehouseId = warehouse.Id,
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

            // Verify stock reservation released
            await using (var context = new AadhiDbContext(CreateSqliteOptions(dbPath)))
            {
                var stock = await context.StockItems.FirstAsync(s => s.ProductId == productId && s.WarehouseId == warehouseId);
                stock.QuantityOnHand.Should().Be(10);
                stock.QuantityReserved.Should().Be(0);

                var movement = await context.StockMovements.FirstOrDefaultAsync(m => m.MovementType == StockMovementType.StockReservationReleased);
                movement.Should().NotBeNull();
                movement!.QuantityChange.Should().Be(3);

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
                var (category, product, warehouse, customer) = await SeedBaseDataAsync(context);

                context.StockItems.Add(new StockItem
                {
                    ProductId = product.Id,
                    WarehouseId = warehouse.Id,
                    QuantityOnHand = 10,
                    QuantityReserved = 0
                });
                await context.SaveChangesAsync();

                var orderService = CreateOrderService(context);
                var order = await orderService.CreateOrderAsync(new CreateOrderRequest
                {
                    WarehouseId = warehouse.Id,
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

    [Fact]
    public async Task LedgerReplay_MatchesCurrentStockBalances_AndBeforeChangeAfterInvariantHolds()
    {
        var dbPath = CreateTempDbPath();
        try
        {
            Guid productId;
            Guid warehouseId;
            Guid order1Id;
            Guid order2Id;

            await using (var context = new AadhiDbContext(CreateSqliteOptions(dbPath)))
            {
                await DatabaseInitializer.InitializeAsync(context, NullLogger.Instance);
                var (category, product, warehouse, customer) = await SeedBaseDataAsync(context);
                productId = product.Id;
                warehouseId = warehouse.Id;

                var stockItem = new StockItem
                {
                    ProductId = product.Id,
                    WarehouseId = warehouse.Id,
                    QuantityOnHand = 20,
                    QuantityReserved = 0
                };
                context.StockItems.Add(stockItem);
                await context.SaveChangesAsync();

                var orderService = CreateOrderService(context);
                var o1 = await orderService.CreateOrderAsync(new CreateOrderRequest
                {
                    WarehouseId = warehouse.Id,
                    ShippingAddress = new Address("Arun Kumar", "9876543210", "123 Main St", null, "Sivakasi", "TN", "626123"),
                    Items = new List<CreateOrderItemRequest> { new() { ProductId = product.Id, Quantity = 5 } }
                });
                order1Id = o1.Id;

                var o2 = await orderService.CreateOrderAsync(new CreateOrderRequest
                {
                    WarehouseId = warehouse.Id,
                    ShippingAddress = new Address("Arun Kumar", "9876543210", "123 Main St", null, "Sivakasi", "TN", "626123"),
                    Items = new List<CreateOrderItemRequest> { new() { ProductId = product.Id, Quantity = 4 } }
                });
                order2Id = o2.Id;
            }

            // Ship order 1
            foreach (var status in new[] { OrderStatus.Confirmed, OrderStatus.Processing, OrderStatus.Packed, OrderStatus.Shipped })
            {
                await using var context = new AadhiDbContext(CreateSqliteOptions(dbPath));
                var orderService = CreateOrderService(context);
                await orderService.UpdateOrderStatusAsync(order1Id, new UpdateOrderStatusRequest { NewStatus = status });
            }

            // Cancel order 2
            await using (var context = new AadhiDbContext(CreateSqliteOptions(dbPath)))
            {
                var orderService = CreateOrderService(context);
                await orderService.UpdateOrderStatusAsync(order2Id, new UpdateOrderStatusRequest { NewStatus = OrderStatus.Cancelled, Reason = "Cancelled" });
            }

            // Verify Ledger Movements Invariants
            await using (var context = new AadhiDbContext(CreateSqliteOptions(dbPath)))
            {
                var movements = await context.StockMovements
                    .Where(m => m.ProductId == productId && m.WarehouseId == warehouseId)
                    .OrderBy(m => m.CreatedAtUtc)
                    .ToListAsync();

                movements.Should().NotBeEmpty();

                // Check Before + Change == After for on-hand modifying movements
                foreach (var m in movements)
                {
                    if (m.MovementType is StockMovementType.Sale or StockMovementType.Purchase or StockMovementType.Adjustment or StockMovementType.TransferIn or StockMovementType.TransferOut)
                    {
                        (m.QuantityBefore + m.QuantityChange).Should().Be(m.QuantityAfter);
                    }
                    else if (m.MovementType is StockMovementType.StockReserved or StockMovementType.StockReservationReleased)
                    {
                        m.QuantityBefore.Should().Be(m.QuantityAfter); // On-hand does not change
                    }
                }

                // Check final StockItem matches exactly 15 on hand, 0 reserved
                var stock = await context.StockItems.FirstAsync(s => s.ProductId == productId && s.WarehouseId == warehouseId);
                stock.QuantityOnHand.Should().Be(15);
                stock.QuantityReserved.Should().Be(0);
                stock.QuantityAvailable.Should().Be(15);
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

    private static async Task<(Category, Product, Warehouse, Customer)> SeedBaseDataAsync(AadhiDbContext context)
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

        var warehouse = new Warehouse
        {
            Code = "WH-MAIN",
            Name = "Main Sivakasi Warehouse",
            IsPrimary = true,
            IsActive = true
        };
        context.Warehouses.Add(warehouse);

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
        return (category, product, warehouse, customer);
    }

    private static string CreateTempDbPath() => Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");

    private static DbContextOptions<AadhiDbContext> CreateSqliteOptions(string databasePath) =>
        new DbContextOptionsBuilder<AadhiDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;

    private static void DeleteDb(string path)
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { path, $"{path}-wal", $"{path}-shm" })
        {
            if (File.Exists(file))
            {
                try { File.Delete(file); } catch { }
            }
        }
    }

    private sealed class TestCurrentUserService : ICurrentUserService
    {
        public string? UserId => "test-user";
        public string? UserName => "Test User";
        public string? Email => "test@example.com";
        public string? Role => "Admin";
        public string? IpAddress => null;
        public string? UserAgent => null;
        public string CorrelationId => "order-test";
        public bool IsAuthenticated => true;
    }
}
