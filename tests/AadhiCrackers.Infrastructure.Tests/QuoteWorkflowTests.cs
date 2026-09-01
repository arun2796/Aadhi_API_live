using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Application.Services;
using AadhiCrackers.Contracts.Inventory;
using AadhiCrackers.Contracts.Orders;
using AadhiCrackers.Contracts.Quotes;
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

public class QuoteWorkflowTests
{
    private static string CreateTempDbPath() =>
        Path.Combine(Path.GetTempPath(), $"aadhi_quote_test_{Guid.NewGuid():N}.db");

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

    private static (OrderService orderService, QuoteService quoteService, PurchaseService purchaseService, InventoryService inventoryService) CreateServices(AadhiDbContext context)
    {
        var currentUser = new TestCurrentUserService();
        var outbox = new OutboxService(context);
        var auditLog = new AuditLogService(context, currentUser, outbox);
        var numberGen = new BusinessNumberGenerator(context);

        var orderService = new OrderService(
            context,
            currentUser,
            auditLog,
            outbox,
            numberGen);

        var quoteService = new QuoteService(
            context,
            currentUser,
            auditLog,
            numberGen,
            outbox,
            orderService);

        var purchaseService = new PurchaseService(
            context,
            currentUser,
            auditLog,
            numberGen);

        var inventoryService = new InventoryService(
            context,
            currentUser,
            auditLog);

        return (orderService, quoteService, purchaseService, inventoryService);
    }

    [Fact]
    public async Task CreateQuote_PersistsQuote_AndComputesAccurateLineTotals()
    {
        var dbPath = CreateTempDbPath();
        try
        {
            await using var context = new AadhiDbContext(CreateSqliteOptions(dbPath));
            await DatabaseInitializer.InitializeAsync(context, NullLogger.Instance);

            var (category, product, warehouse, customer) = await SeedBaseDataAsync(context);
            var (_, quoteService, _, _) = CreateServices(context);

            var request = new CreateQuoteRequest
            {
                CustomerId = customer.Id,
                ExpiryDateUtc = DateTime.UtcNow.AddDays(15),
                Notes = "Wholesale festive discount quote",
                DiscountAmount = 50,
                Items = new List<CreateQuoteItemRequest>
                {
                    new()
                    {
                        ProductId = product.Id,
                        Quantity = 10,
                        CustomUnitPrice = 100m,
                        DiscountPercentage = 10 // 100 * 10 - 10% = 900
                    }
                }
            };

            var quoteDto = await quoteService.CreateQuoteAsync(request);

            quoteDto.Should().NotBeNull();
            quoteDto.QuoteNumber.Should().StartWith("QUO-");
            quoteDto.Subtotal.Should().Be(900m);
            quoteDto.Discount.Should().Be(50m);
            quoteDto.GrandTotal.Should().Be(900m - 50m + (900m * 0.18m)); // 850 + 162 = 1012
            quoteDto.Status.Should().Be("Draft");

            var persisted = await context.Quotes.Include(q => q.Items).FirstOrDefaultAsync(q => q.Id == quoteDto.Id);
            persisted.Should().NotBeNull();
            persisted!.Items.Should().HaveCount(1);
            persisted.Items.First().Quantity.Should().Be(10);
        }
        finally
        {
            DeleteDb(dbPath);
        }
    }

    [Fact]
    public async Task ConvertQuoteToOrder_ValidQuote_ReservesStockCreatesOrderAndInvoice_AndMarksQuoteConverted()
    {
        var dbPath = CreateTempDbPath();
        try
        {
            await using var context = new AadhiDbContext(CreateSqliteOptions(dbPath));
            await DatabaseInitializer.InitializeAsync(context, NullLogger.Instance);

            var (category, product, warehouse, customer) = await SeedBaseDataAsync(context);
            var (_, quoteService, _, _) = CreateServices(context);

            // Add stock
            var stockItem = new StockItem
            {
                ProductId = product.Id,
                WarehouseId = warehouse.Id,
                QuantityOnHand = 50,
                QuantityReserved = 0
            };
            context.StockItems.Add(stockItem);
            await context.SaveChangesAsync();

            var quote = await quoteService.CreateQuoteAsync(new CreateQuoteRequest
            {
                CustomerId = customer.Id,
                ExpiryDateUtc = DateTime.UtcNow.AddDays(7),
                DiscountAmount = 0,
                Items = new List<CreateQuoteItemRequest>
                {
                    new()
                    {
                        ProductId = product.Id,
                        Quantity = 15,
                        CustomUnitPrice = 200m,
                        DiscountPercentage = 0
                    }
                }
            });

            var order = await quoteService.ConvertQuoteToOrderAsync(quote.Id);

            order.Should().NotBeNull();
            order.OrderNumber.Should().StartWith("ORD-");
            order.Items.Should().HaveCount(1);
            order.Items.First().Quantity.Should().Be(15);

            // Verify Quote status updated to Converted
            var updatedQuote = await context.Quotes.FirstOrDefaultAsync(q => q.Id == quote.Id);
            updatedQuote.Should().NotBeNull();
            updatedQuote!.Status.Should().Be(QuoteStatus.Converted);
            updatedQuote.ConvertedOrderId.Should().Be(order.Id);

            // Verify Stock reserved
            var updatedStock = await context.StockItems.FirstOrDefaultAsync(s => s.Id == stockItem.Id);
            updatedStock!.QuantityReserved.Should().Be(15);
            updatedStock.QuantityAvailable.Should().Be(35);

            // Verify Invoice generated
            var invoice = await context.Invoices.FirstOrDefaultAsync(i => i.OrderId == order.Id);
            invoice.Should().NotBeNull();
            invoice!.InvoiceNumber.Should().StartWith("INV-");

            // Verify Outbox message
            var outbox = await context.OutboxMessages.FirstOrDefaultAsync(o => o.Type == "OrderPlaced");
            outbox.Should().NotBeNull();
            outbox!.PayloadJson.Should().Contain(order.OrderNumber);
        }
        finally
        {
            DeleteDb(dbPath);
        }
    }

    [Fact]
    public async Task ConvertQuoteToOrder_AlreadyConvertedQuote_ThrowsDomainException()
    {
        var dbPath = CreateTempDbPath();
        try
        {
            await using var context = new AadhiDbContext(CreateSqliteOptions(dbPath));
            await DatabaseInitializer.InitializeAsync(context, NullLogger.Instance);

            var (category, product, warehouse, customer) = await SeedBaseDataAsync(context);
            var (_, quoteService, _, _) = CreateServices(context);

            context.StockItems.Add(new StockItem
            {
                ProductId = product.Id,
                WarehouseId = warehouse.Id,
                QuantityOnHand = 20,
                QuantityReserved = 0
            });
            await context.SaveChangesAsync();

            var quote = await quoteService.CreateQuoteAsync(new CreateQuoteRequest
            {
                CustomerId = customer.Id,
                Items = new List<CreateQuoteItemRequest>
                {
                    new() { ProductId = product.Id, Quantity = 5 }
                }
            });

            await quoteService.ConvertQuoteToOrderAsync(quote.Id);

            // Second conversion attempt must fail
            var act = async () => await quoteService.ConvertQuoteToOrderAsync(quote.Id);
            await act.Should().ThrowAsync<DomainException>()
                .WithMessage("*already been converted*");
        }
        finally
        {
            DeleteDb(dbPath);
        }
    }

    [Fact]
    public async Task ConvertQuoteToOrder_ExpiredQuote_ThrowsDomainException()
    {
        var dbPath = CreateTempDbPath();
        try
        {
            await using var context = new AadhiDbContext(CreateSqliteOptions(dbPath));
            await DatabaseInitializer.InitializeAsync(context, NullLogger.Instance);

            var (category, product, warehouse, customer) = await SeedBaseDataAsync(context);
            var (_, quoteService, _, _) = CreateServices(context);

            context.StockItems.Add(new StockItem
            {
                ProductId = product.Id,
                WarehouseId = warehouse.Id,
                QuantityOnHand = 20,
                QuantityReserved = 0
            });
            await context.SaveChangesAsync();

            var quote = await quoteService.CreateQuoteAsync(new CreateQuoteRequest
            {
                CustomerId = customer.Id,
                ExpiryDateUtc = DateTime.UtcNow.AddMinutes(-10), // Expired
                Items = new List<CreateQuoteItemRequest>
                {
                    new() { ProductId = product.Id, Quantity = 5 }
                }
            });

            var act = async () => await quoteService.ConvertQuoteToOrderAsync(quote.Id);
            await act.Should().ThrowAsync<DomainException>()
                .WithMessage("*expired*");
        }
        finally
        {
            DeleteDb(dbPath);
        }
    }

    [Fact]
    public async Task ConvertQuoteToOrder_InsufficientStock_ThrowsDomainException()
    {
        var dbPath = CreateTempDbPath();
        try
        {
            await using var context = new AadhiDbContext(CreateSqliteOptions(dbPath));
            await DatabaseInitializer.InitializeAsync(context, NullLogger.Instance);

            var (category, product, warehouse, customer) = await SeedBaseDataAsync(context);
            var (_, quoteService, _, _) = CreateServices(context);

            context.StockItems.Add(new StockItem
            {
                ProductId = product.Id,
                WarehouseId = warehouse.Id,
                QuantityOnHand = 5,
                QuantityReserved = 0
            });
            await context.SaveChangesAsync();

            var quote = await quoteService.CreateQuoteAsync(new CreateQuoteRequest
            {
                CustomerId = customer.Id,
                Items = new List<CreateQuoteItemRequest>
                {
                    new() { ProductId = product.Id, Quantity = 50 } // Requires 50, only 5 available
                }
            });

            var act = async () => await quoteService.ConvertQuoteToOrderAsync(quote.Id);
            await act.Should().ThrowAsync<DomainException>()
                .WithMessage("*Insufficient stock*");
        }
        finally
        {
            DeleteDb(dbPath);
        }
    }

    [Fact]
    public async Task GetStockTransfers_ReturnsStockTransferMovements()
    {
        var dbPath = CreateTempDbPath();
        try
        {
            await using var context = new AadhiDbContext(CreateSqliteOptions(dbPath));
            await DatabaseInitializer.InitializeAsync(context, NullLogger.Instance);

            var (category, product, warehouse1, customer) = await SeedBaseDataAsync(context);
            var warehouse2 = new Warehouse
            {
                Name = "Madurai Hub",
                Code = "WH-MDU",
                Address = "456 North St, Madurai, TN, 625001"
            };
            context.Warehouses.Add(warehouse2);

            var stock1 = new StockItem
            {
                ProductId = product.Id,
                WarehouseId = warehouse1.Id,
                QuantityOnHand = 100,
                QuantityReserved = 0
            };
            var stock2 = new StockItem
            {
                ProductId = product.Id,
                WarehouseId = warehouse2.Id,
                QuantityOnHand = 0,
                QuantityReserved = 0
            };
            context.StockItems.AddRange(stock1, stock2);
            await context.SaveChangesAsync();

            var (_, _, _, inventoryService) = CreateServices(context);

            await inventoryService.TransferStockAsync(new StockTransferRequest
            {
                ProductId = product.Id,
                FromWarehouseId = warehouse1.Id,
                ToWarehouseId = warehouse2.Id,
                Quantity = 30,
                Reason = "Inter-warehouse transfer"
            });

            var transfers = await inventoryService.GetStockTransfersAsync();
            transfers.Should().NotBeNull();
            transfers.Items.Should().HaveCount(2); // TransferOut + TransferIn
            transfers.TotalCount.Should().Be(2);
        }
        finally
        {
            DeleteDb(dbPath);
        }
    }

    private static async Task<(Category category, Product product, Warehouse warehouse, Customer customer)> SeedBaseDataAsync(AadhiDbContext context)
    {
        var category = new Category
        {
            Name = "Sparklers",
            Slug = "sparklers",
            Description = "Standard sparklers",
            DisplayOrder = 1,
            IsActive = true
        };
        context.Categories.Add(category);

        var product = new Product
        {
            Name = "10cm Electric Sparklers",
            SKU = "SPK-10CM-01",
            Slug = "10cm-electric-sparklers",
            ShortDescription = "Electric sparklers box",
            Description = "Long lasting sparklers",
            Category = category,
            Price = Money.FromDecimal(120m),
            CompareAtPrice = Money.FromDecimal(150m),
            CostPrice = Money.FromDecimal(60m),
            TaxRate = 18m,
            StockQuantity = 100,
            ReservedQuantity = 0,
            ReorderLevel = 10,
            Unit = "Box",
            IsActive = true
        };
        context.Products.Add(product);

        var warehouse = new Warehouse
        {
            Name = "Main Sivakasi Depot",
            Code = "WH-SVK-01",
            Address = "1 Depot Rd, Sivakasi, TN, 626123"
        };
        context.Warehouses.Add(warehouse);

        var customer = new Customer
        {
            UserId = "user-123",
            FirstName = "Suresh",
            LastName = "Raina",
            Email = "suresh.raina@aadhicrackers.com",
            Phone = "9876501234",
            IsActive = true
        };
        context.Customers.Add(customer);

        await context.SaveChangesAsync();
        return (category, product, warehouse, customer);
    }

    private sealed class TestCurrentUserService : ICurrentUserService
    {
        public string? UserId => "test-admin-id";
        public string? UserName => "Admin";
        public string? Email => "admin@aadhicrackers.com";
        public string? Role => "Admin";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "UnitTestRunner";
        public string CorrelationId => "test-correlation-id";
        public bool IsAuthenticated => true;
    }
}
