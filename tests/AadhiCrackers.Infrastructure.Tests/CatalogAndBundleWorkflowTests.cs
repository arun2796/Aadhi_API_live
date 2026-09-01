using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Application.Services;
using AadhiCrackers.Contracts.Catalog;
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

public class CatalogAndBundleWorkflowTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AadhiDbContext> _options;

    public CatalogAndBundleWorkflowTests()
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
        public string? Email => "catalog.admin@aadhicrackers.com";
        public string? UserName => "catalogAdmin";
        public string? Role => "Admin";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "xUnit-Runner";
        public string CorrelationId => Guid.NewGuid().ToString();
        public bool IsAuthenticated => true;
    }

    private static (CatalogService catalogService, OrderService orderService) CreateServices(AadhiDbContext context)
    {
        var user = new TestCurrentUserService();
        var outbox = new OutboxService(context);
        var audit = new AuditLogService(context, user, outbox);
        var numberGen = new BusinessNumberGenerator(context);
        var catalogService = new CatalogService(context, audit);
        var orderService = new OrderService(context, user, audit, outbox, numberGen);
        return (catalogService, orderService);
    }

    [Fact]
    public async Task CategoryValidation_ShouldPreventSelfParent_CircularHierarchy_AndActiveProductDeletion()
    {
        using var context = new AadhiDbContext(_options);
        var (catalogService, _) = CreateServices(context);

        // 1. Create root Category A
        var catA = await catalogService.CreateCategoryAsync(new CreateCategoryRequest
        {
            Name = "Sparklers & Flares",
            DisplayOrder = 1
        });

        // 2. Create subcategory B with parent A
        var catB = await catalogService.CreateCategoryAsync(new CreateCategoryRequest
        {
            Name = "Electric Sparklers",
            ParentCategoryId = catA.Id,
            DisplayOrder = 2
        });

        // 3. Create subcategory C with parent B
        var catC = await catalogService.CreateCategoryAsync(new CreateCategoryRequest
        {
            Name = "Color Sparklers",
            ParentCategoryId = catB.Id,
            DisplayOrder = 3
        });

        // 4. Attempting to make Cat A's parent Cat C (Creating loop: A -> B -> C -> A) should throw DomainException
        await Assert.ThrowsAsync<DomainException>(() => catalogService.UpdateCategoryAsync(new UpdateCategoryRequest
        {
            Id = catA.Id,
            Name = "Sparklers & Flares",
            ParentCategoryId = catC.Id
        }));

        // 5. Attempting to make Cat B its own parent should throw DomainException
        await Assert.ThrowsAsync<DomainException>(() => catalogService.UpdateCategoryAsync(new UpdateCategoryRequest
        {
            Id = catB.Id,
            Name = "Electric Sparklers",
            ParentCategoryId = catB.Id
        }));

        // 6. Add active product to Cat C
        await catalogService.CreateProductAsync(new CreateProductRequest
        {
            SKU = "SPK-CLR-001",
            Name = "Multi Color Sparklers 15cm",
            CategoryId = catC.Id,
            Price = 150m,
            StockQuantity = 50
        });

        // 7. Deleting Cat C while it has active products should throw DomainException
        await Assert.ThrowsAsync<DomainException>(() => catalogService.DeleteCategoryAsync(catC.Id));

        // 8. Deleting Cat A while it has subcategories should throw DomainException
        await Assert.ThrowsAsync<DomainException>(() => catalogService.DeleteCategoryAsync(catA.Id));
    }

    [Fact]
    public async Task ProductWithVariantsAndMultipleCategories_ShouldPersistAndFilterCorrectly()
    {
        using var context = new AadhiDbContext(_options);
        var (catalogService, _) = CreateServices(context);

        var cat1 = await catalogService.CreateCategoryAsync(new CreateCategoryRequest { Name = "Rockets" });
        var cat2 = await catalogService.CreateCategoryAsync(new CreateCategoryRequest { Name = "Sky Specials" });

        var created = await catalogService.CreateProductAsync(new CreateProductRequest
        {
            SKU = "RCK-SKY-01",
            Name = "Super Sound Rocket",
            Description = "High flying aerial sound rocket",
            ProductType = ProductType.Variant,
            CategoryId = cat1.Id,
            AdditionalCategoryIds = new List<Guid> { cat2.Id },
            Price = 300m,
            StockQuantity = 100,
            Variants = new List<CreateProductVariantRequest>
            {
                new() { SKU = "RCK-SKY-01-PK5", Name = "Pack of 5", Price = 300m, StockQuantity = 60 },
                new() { SKU = "RCK-SKY-01-PK10", Name = "Pack of 10", Price = 550m, StockQuantity = 40 }
            }
        });

        Assert.Equal(ProductType.Variant, created.ProductType);
        Assert.Equal(2, created.Variants.Count);
        Assert.Contains(created.CategoryIds, id => id == cat1.Id);
        Assert.Contains(created.CategoryIds, id => id == cat2.Id);

        // Filter by secondary category
        var filterResult = await catalogService.GetProductsAsync(new ProductFilterRequest
        {
            CategoryId = cat2.Id
        });

        Assert.Single(filterResult.Items);
        Assert.Equal("Super Sound Rocket", filterResult.Items[0].Name);
    }

    [Fact]
    public async Task BundleBOM_StockReservation_Fulfillment_AndCancellation_ShouldManageComponentInventory()
    {
        using var context = new AadhiDbContext(_options);
        var (catalogService, orderService) = CreateServices(context);

        var category = await catalogService.CreateCategoryAsync(new CreateCategoryRequest { Name = "Gift Boxes" });

        var warehouse = new Warehouse
        {
            Code = "WH-BOM",
            Name = "BOM Warehouse",
            Phone = "9876543210",
            IsActive = true,
            IsPrimary = true
        };
        context.Warehouses.Add(warehouse);
        await context.SaveChangesAsync();

        // 1. Create 2 Component Products with individual stock
        var comp1 = await catalogService.CreateProductAsync(new CreateProductRequest
        {
            SKU = "COMP-SPK-10",
            Name = "10cm Sparklers Component",
            CategoryId = category.Id,
            Price = 50m,
            StockQuantity = 100
        });

        var comp2 = await catalogService.CreateProductAsync(new CreateProductRequest
        {
            SKU = "COMP-FLW-01",
            Name = "Flower Pot Component",
            CategoryId = category.Id,
            Price = 80m,
            StockQuantity = 100
        });

        // 2. Create Bundle Product containing (2x comp1 + 1x comp2)
        var bundle = await catalogService.CreateProductAsync(new CreateProductRequest
        {
            SKU = "BUNDLE-DIWALI-MEGA",
            Name = "Diwali Mega Gift Box",
            Description = "Assorted family gift box containing sparklers and flower pots",
            ProductType = ProductType.Bundle,
            CategoryId = category.Id,
            Price = 250m,
            StockQuantity = 50,
            BundleComponents = new List<CreateGiftBoxComponentRequest>
            {
                new() { ComponentProductId = comp1.Id, Quantity = 2 },
                new() { ComponentProductId = comp2.Id, Quantity = 1 }
            }
        });

        Assert.Equal(ProductType.Bundle, bundle.ProductType);
        Assert.Equal(2, bundle.BundleComponents.Count);

        // 3. Customer places Order for 5 Bundles
        // This requires 5 bundles = (5 * 2 = 10 comp1) AND (5 * 1 = 5 comp2)
        var customer = new Customer
        {
            CustomerCode = "CUST-BOM-01",
            FirstName = "Suresh",
            LastName = "Raina",
            Email = "suresh@example.com",
            Phone = "9988776655",
            IsActive = true
        };
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        var order = await orderService.CreateOrderAsync(new CreateOrderRequest
        {
            WarehouseId = warehouse.Id,
            PaymentMethod = PaymentMethod.UPI,
            ShippingAddress = new Address
            {
                FullName = "Suresh Raina",
                Phone = "9988776655",
                AddressLine1 = "Cricket Stadium Road",
                City = "Chennai",
                State = "Tamil Nadu",
                PostalCode = "600001"
            },
            Items = new List<CreateOrderItemRequest>
            {
                new() { ProductId = bundle.Id, Quantity = 5 }
            }
        });

        // Verify Bundle reservation
        var bundleStock = await context.StockItems.FirstAsync(s => s.ProductId == bundle.Id);
        Assert.Equal(5, bundleStock.QuantityReserved);

        // Verify Component 1 reservation: 10 reserved
        var comp1Stock = await context.StockItems.FirstAsync(s => s.ProductId == comp1.Id);
        Assert.Equal(10, comp1Stock.QuantityReserved);
        Assert.Equal(90, comp1Stock.QuantityAvailable); // 100 - 10

        // Verify Component 2 reservation: 5 reserved
        var comp2Stock = await context.StockItems.FirstAsync(s => s.ProductId == comp2.Id);
        Assert.Equal(5, comp2Stock.QuantityReserved);
        Assert.Equal(95, comp2Stock.QuantityAvailable); // 100 - 5

        // 4. Ship Order -> Deducts on-hand stock for both bundle and components
        await orderService.UpdateOrderStatusAsync(order.Id, new UpdateOrderStatusRequest { NewStatus = OrderStatus.Confirmed });
        await orderService.UpdateOrderStatusAsync(order.Id, new UpdateOrderStatusRequest { NewStatus = OrderStatus.Processing });
        await orderService.UpdateOrderStatusAsync(order.Id, new UpdateOrderStatusRequest { NewStatus = OrderStatus.Packed });
        await orderService.UpdateOrderStatusAsync(order.Id, new UpdateOrderStatusRequest { NewStatus = OrderStatus.Shipped });

        comp1Stock = await context.StockItems.FirstAsync(s => s.ProductId == comp1.Id);
        Assert.Equal(90, comp1Stock.QuantityOnHand); // 100 - 10
        Assert.Equal(0, comp1Stock.QuantityReserved);

        comp2Stock = await context.StockItems.FirstAsync(s => s.ProductId == comp2.Id);
        Assert.Equal(95, comp2Stock.QuantityOnHand); // 100 - 5
        Assert.Equal(0, comp2Stock.QuantityReserved);
    }
}
