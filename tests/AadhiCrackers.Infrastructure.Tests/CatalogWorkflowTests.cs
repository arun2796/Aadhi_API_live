using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Application.Services;
using AadhiCrackers.Contracts.Catalog;
using AadhiCrackers.Domain.Exceptions;
using AadhiCrackers.Infrastructure.Persistence;
using AadhiCrackers.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AadhiCrackers.Infrastructure.Tests;

public class CatalogWorkflowTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AadhiDbContext> _options;

    public CatalogWorkflowTests()
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
    public async Task ProductWithMultipleCategories_ShouldPersistAndFilterCorrectly()
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
            CategoryId = cat1.Id,
            AdditionalCategoryIds = new List<Guid> { cat2.Id },
            Price = 300m,
            StockQuantity = 100
        });

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

}
