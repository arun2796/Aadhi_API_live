using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Application.Services;
using AadhiCrackers.Contracts.Finance;
using AadhiCrackers.Contracts.Orders;
using AadhiCrackers.Domain.Entities;
using AadhiCrackers.Domain.Enums;
using AadhiCrackers.Domain.ValueObjects;
using AadhiCrackers.Infrastructure.Persistence;
using AadhiCrackers.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AadhiCrackers.Infrastructure.Tests;

public class PromotionFinanceAndOutboxTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AadhiDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditLogService _auditLog;
    private readonly IOutboxService _outbox;
    private readonly IBusinessNumberGenerator _numberGenerator;
    private readonly IOrderService _orderService;
    private readonly IFinanceService _financeService;
    private readonly IReportService _reportService;

    public PromotionFinanceAndOutboxTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AadhiDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AadhiDbContext(options);
        _context.Database.EnsureCreated();

        _currentUser = new TestCurrentUserService();
        _outbox = new OutboxService(_context);
        _auditLog = new AuditLogService(_context, _currentUser, _outbox);
        _numberGenerator = new BusinessNumberGenerator(_context);
        _orderService = new OrderService(_context, _currentUser, _auditLog, _outbox, _numberGenerator);
        _financeService = new FinanceService(_context, _currentUser, _auditLog, _outbox, _numberGenerator);
        _reportService = new ReportService(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private sealed class TestCurrentUserService : ICurrentUserService
    {
        public string? UserId => Guid.NewGuid().ToString();
        public string? Email => "customer@aadhicrackers.com";
        public string? UserName => "CustomerUser";
        public string? Role => "Customer";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "xUnit";
        public string CorrelationId => Guid.NewGuid().ToString();
        public bool IsAuthenticated => true;
    }

    [Fact]
    public async Task Promotion_PerCustomerLimitAndRollback_WorksAccurately()
    {
        // 1. Arrange customer, warehouse, category, product, promotion
        var customer = new Customer
        {
            UserId = _currentUser.UserId ?? Guid.NewGuid().ToString(),
            CustomerCode = "CUST-001",
            FirstName = "Karthik",
            LastName = "Raja",
            Email = "karthik@test.com",
            Phone = "9876543210"
        };
        _context.Customers.Add(customer);

        var warehouse = new Warehouse
        {
            Code = "WH-01",
            Name = "Hub",
            Phone = "9876543210",
            IsActive = true,
            IsPrimary = true
        };
        _context.Warehouses.Add(warehouse);

        var category = new Category { Name = "SkyShots", Slug = "skyshots", IsActive = true };
        _context.Categories.Add(category);

        var product = new Product
        {
            CategoryId = category.Id,
            SKU = "SKY-777",
            Name = "7 Star Sky Shot",
            Price = Money.FromDecimal(1000m),
            CostPrice = Money.FromDecimal(500m),
            StockQuantity = 50,
            ReservedQuantity = 0,
            ReorderLevel = 5,
            IsActive = true
        };
        _context.Products.Add(product);
        _context.StockItems.Add(new StockItem
        {
            ProductId = product.Id,
            WarehouseId = warehouse.Id,
            QuantityOnHand = 50,
            QuantityReserved = 0,
            ReorderLevel = 5
        });

        var promo = new Promotion
        {
            Code = "SAVE10",
            Name = "10% Discount",
            DiscountType = DiscountType.Percentage,
            DiscountValue = 10m,
            PerCustomerLimit = 1,
            UsageLimit = 100,
            UsedCount = 0,
            IsActive = true
        };
        _context.Promotions.Add(promo);
        await _context.SaveChangesAsync();

        // 2. First order with SAVE10
        var order1 = await _orderService.CreateOrderAsync(new CreateOrderRequest
        {
            WarehouseId = warehouse.Id,
            CouponCode = "SAVE10",
            PaymentMethod = PaymentMethod.COD,
            Items = new List<CreateOrderItemRequest>
            {
                new CreateOrderItemRequest { ProductId = product.Id, Quantity = 4 }
            },
            ShippingAddress = new Address
            {
                FullName = "Karthik Raja",
                Phone = "9876543210",
                AddressLine1 = "12 Main Road",
                City = "Sivakasi",
                State = "Tamil Nadu",
                PostalCode = "626123"
            }
        });

        // 4 * 1000 = 4000 subtotal, 10% discount = 400
        Assert.Equal(400m, order1.Discount);
        var promoAfterOrder1 = await _context.Promotions.FirstAsync(p => p.Code == "SAVE10");
        Assert.Equal(1, promoAfterOrder1.UsedCount);

        // 3. Second order with SAVE10 by same customer -> PerCustomerLimit reached, discount is 0
        var order2 = await _orderService.CreateOrderAsync(new CreateOrderRequest
        {
            WarehouseId = warehouse.Id,
            CouponCode = "SAVE10",
            PaymentMethod = PaymentMethod.COD,
            Items = new List<CreateOrderItemRequest>
            {
                new CreateOrderItemRequest { ProductId = product.Id, Quantity = 4 }
            },
            ShippingAddress = new Address
            {
                FullName = "Karthik Raja",
                Phone = "9876543210",
                AddressLine1 = "12 Main Road",
                City = "Sivakasi",
                State = "Tamil Nadu",
                PostalCode = "626123"
            }
        });

        Assert.Equal(0m, order2.Discount);

        // 4. Cancel Order 1 -> UsedCount rolls back, Invoices cancelled
        await _orderService.UpdateOrderStatusAsync(order1.Id, new UpdateOrderStatusRequest
        {
            NewStatus = OrderStatus.Cancelled,
            Reason = "Customer request cancellation"
        });

        var promoAfterCancel = await _context.Promotions.FirstAsync(p => p.Code == "SAVE10");
        Assert.Equal(0, promoAfterCancel.UsedCount);

        var invoices = await _context.Invoices.Where(i => i.OrderId == order1.Id).ToListAsync();
        Assert.All(invoices, inv => Assert.Equal(InvoiceStatus.Cancelled, inv.Status));
    }

    [Fact]
    public async Task Finance_TrueCOGSAndPAndL_CalculatesAccurately()
    {
        // 1. Arrange customer, warehouse, category, product
        var customer = new Customer
        {
            UserId = _currentUser.UserId ?? Guid.NewGuid().ToString(),
            CustomerCode = "CUST-002",
            FirstName = "Anand",
            LastName = "Kumar",
            Email = "anand@test.com",
            Phone = "9876543212"
        };
        _context.Customers.Add(customer);

        var warehouse = new Warehouse
        {
            Code = "WH-02",
            Name = "Depot",
            Phone = "9876543212",
            IsActive = true,
            IsPrimary = true
        };
        _context.Warehouses.Add(warehouse);

        var category = new Category { Name = "Rockets", Slug = "rockets", IsActive = true };
        _context.Categories.Add(category);

        var product = new Product
        {
            CategoryId = category.Id,
            SKU = "RCK-LUNA",
            Name = "Lunar Rocket Pack",
            Price = Money.FromDecimal(500m),
            CostPrice = Money.FromDecimal(250m),
            StockQuantity = 100,
            ReservedQuantity = 0,
            ReorderLevel = 10,
            IsActive = true
        };
        _context.Products.Add(product);
        _context.StockItems.Add(new StockItem
        {
            ProductId = product.Id,
            WarehouseId = warehouse.Id,
            QuantityOnHand = 100,
            QuantityReserved = 0,
            ReorderLevel = 10
        });
        await _context.SaveChangesAsync();

        // 2. Order 2 units: Subtotal = 1000, COGS = 2 * 250 = 500
        var order = await _orderService.CreateOrderAsync(new CreateOrderRequest
        {
            WarehouseId = warehouse.Id,
            PaymentMethod = PaymentMethod.COD,
            Items = new List<CreateOrderItemRequest>
            {
                new CreateOrderItemRequest { ProductId = product.Id, Quantity = 2 }
            },
            ShippingAddress = new Address
            {
                FullName = "Anand Kumar",
                Phone = "9876543212",
                AddressLine1 = "45 Park Street",
                City = "Madurai",
                State = "Tamil Nadu",
                PostalCode = "625001"
            }
        });

        // 3. Log Expense: 100
        await _financeService.CreateExpenseAsync(new CreateExpenseRequest
        {
            Category = ExpenseCategory.Packaging,
            Description = "Corrugated packing boxes",
            Amount = 100m,
            Tax = 0m,
            PaymentMethod = PaymentMethod.Cash
        });

        // 4. Check P&L
        var pl = await _financeService.GetProfitLossAsync();
        Assert.Equal(1000m, pl.TotalRevenue);      // Net Sales (1000)
        Assert.Equal(500m, pl.CostOfGoodsSold);   // Real COGS (2 * 250)
        Assert.Equal(500m, pl.GrossProfit);        // 1000 - 500 = 500
        Assert.Equal(100m, pl.TotalExpenses);      // 100
        Assert.Equal(400m, pl.NetProfit);          // 500 - 100 = 400
        Assert.Equal(40m, pl.ProfitMargin);        // (400 / 1000) * 100 = 40%

        // 5. Check Live Reports (no fabricated numbers)
        var topCats = await _reportService.GetTopSellingCategoriesAsync();
        Assert.Single(topCats);
        Assert.Equal("Rockets", topCats[0].CategoryName);
        Assert.Equal(1000m, topCats[0].Revenue);
        Assert.Equal(100, topCats[0].Percentage);

        var topProds = await _reportService.GetTopSellingProductsAsync(5);
        Assert.Single(topProds);
        Assert.Equal("Lunar Rocket Pack", topProds[0].ProductName);
        Assert.Equal(2, topProds[0].UnitsSold);
        Assert.Equal(1000m, topProds[0].Revenue);
    }
}
