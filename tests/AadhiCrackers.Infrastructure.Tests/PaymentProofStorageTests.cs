using AadhiCrackers.Application.Common;
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

namespace AadhiCrackers.Infrastructure.Tests;


public class PaymentProofStorageTests
{
    [Fact]
    public async Task CreateOrder_UploadsBase64Screenshot_AndStoresOnlyTheUrl()
    {
        var dbPath = CreateTempDbPath();
        try
        {
            await using var context = new AadhiDbContext(CreateSqliteOptions(dbPath));
            await context.Database.EnsureCreatedAsync();
            var product = await SeedProductAsync(context);

            var storage = new RecordingFileStorage();
            var orderService = CreateOrderService(context, storage);

            var dto = await orderService.CreateOrderAsync(new CreateOrderRequest
            {
                ShippingAddress = SampleAddress,
                PaymentMethod = PaymentMethod.UPI,
                PaymentScreenshotBase64 = $"data:image/png;base64,{Convert.ToBase64String(OnePixelPng)}",
                Items = new List<CreateOrderItemRequest> { new() { ProductId = product.Id, Quantity = 1 } }
            });

            storage.Saved.Should().HaveCount(1, "the screenshot must be uploaded, not inlined");
            storage.Saved[0].Folder.Should().Be("payment-proofs");
            storage.Saved[0].ContentType.Should().Be("image/png");

            var stored = await context.Orders.AsNoTracking().FirstAsync(o => o.Id == dto.Id);
            stored.PaymentScreenshotUrl.Should().Be(storage.Saved[0].ReturnedUrl);
            stored.PaymentScreenshotUrl.Should().NotStartWith("data:", "no image bytes belong in the order row");
        }
        finally
        {
            DeleteDb(dbPath);
        }
    }

    [Fact]
    public async Task CreateOrder_LeavesAnExistingUrlAlone()
    {
        var dbPath = CreateTempDbPath();
        try
        {
            await using var context = new AadhiDbContext(CreateSqliteOptions(dbPath));
            await context.Database.EnsureCreatedAsync();
            var product = await SeedProductAsync(context);

            var storage = new RecordingFileStorage();
            var orderService = CreateOrderService(context, storage);

            const string existing = "https://pub-abc123.r2.dev/payment-proofs/already-there.jpg";
            var dto = await orderService.CreateOrderAsync(new CreateOrderRequest
            {
                ShippingAddress = SampleAddress,
                PaymentMethod = PaymentMethod.UPI,
                PaymentScreenshotUrl = existing,
                Items = new List<CreateOrderItemRequest> { new() { ProductId = product.Id, Quantity = 1 } }
            });

            storage.Saved.Should().BeEmpty("an already-stored proof must not be re-uploaded");
            var stored = await context.Orders.AsNoTracking().FirstAsync(o => o.Id == dto.Id);
            stored.PaymentScreenshotUrl.Should().Be(existing);
        }
        finally
        {
            DeleteDb(dbPath);
        }
    }

    [Fact]
    public async Task CreateOrder_RejectsAScreenshotThatIsNotAnImage()
    {
        var dbPath = CreateTempDbPath();
        try
        {
            await using var context = new AadhiDbContext(CreateSqliteOptions(dbPath));
            await context.Database.EnsureCreatedAsync();
            var product = await SeedProductAsync(context);

            var storage = new RecordingFileStorage();
            var orderService = CreateOrderService(context, storage);

            // Valid base64, but the bytes are a text file wearing an image/png label.
            var notAnImage = Convert.ToBase64String("MZ this is definitely not a picture"u8.ToArray());

            var act = () => orderService.CreateOrderAsync(new CreateOrderRequest
            {
                ShippingAddress = SampleAddress,
                PaymentMethod = PaymentMethod.UPI,
                PaymentScreenshotBase64 = $"data:image/png;base64,{notAnImage}",
                Items = new List<CreateOrderItemRequest> { new() { ProductId = product.Id, Quantity = 1 } }
            });

            await act.Should().ThrowAsync<DomainException>().WithMessage("*not a*image*");
            storage.Saved.Should().BeEmpty();
        }
        finally
        {
            DeleteDb(dbPath);
        }
    }

    [Fact]
    public async Task CreateOrder_StillSucceeds_WhenObjectStorageIsDown()
    {
        var dbPath = CreateTempDbPath();
        try
        {
            await using var context = new AadhiDbContext(CreateSqliteOptions(dbPath));
            await context.Database.EnsureCreatedAsync();
            var product = await SeedProductAsync(context);

            var orderService = CreateOrderService(context, new ThrowingFileStorage());

            var dto = await orderService.CreateOrderAsync(new CreateOrderRequest
            {
                ShippingAddress = SampleAddress,
                PaymentMethod = PaymentMethod.UPI,
                PaymentScreenshotBase64 = $"data:image/png;base64,{Convert.ToBase64String(OnePixelPng)}",
                Items = new List<CreateOrderItemRequest> { new() { ProductId = product.Id, Quantity = 1 } }
            });

            // The customer has already paid; losing the order because R2 was unreachable would be
            // far worse than keeping the proof inline until it can be migrated out.
            var stored = await context.Orders.AsNoTracking().FirstAsync(o => o.Id == dto.Id);
            stored.PaymentScreenshotUrl.Should().StartWith("data:image/png;base64,");
        }
        finally
        {
            DeleteDb(dbPath);
        }
    }

    // ---------------------------------------------------------------- helpers

    private static readonly Address SampleAddress =
        new("Arun Kumar", "9876543210", "123 Main St", null, "Sivakasi", "TN", "626123");

    /// <summary>A real 1x1 PNG, so the content sniffer sees genuine PNG magic bytes.</summary>
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private sealed record SavedObject(string Folder, string ContentType, string Extension, long Length, string ReturnedUrl);

    private sealed class RecordingFileStorage : IFileStorageService
    {
        public List<SavedObject> Saved { get; } = new();

        public Task<StoredFileResult> SaveObjectAsync(
            Stream fileStream, string folder, string contentType, string extension, CancellationToken cancellationToken = default)
        {
            using var buffer = new MemoryStream();
            fileStream.CopyTo(buffer);

            var key = $"{folder}/{Guid.NewGuid():N}{extension}";
            var url = $"https://pub-test.r2.dev/{key}";
            Saved.Add(new SavedObject(folder, contentType, extension, buffer.Length, url));

            return Task.FromResult(new StoredFileResult(url, key, contentType, buffer.Length));
        }

        public Task<string> SaveFileAsync(Stream fileStream, string fileName, string folder = "products", CancellationToken cancellationToken = default)
            => throw new NotSupportedException("SaveObjectAsync is the path under test.");

        public Task<bool> DeleteFileAsync(string relativePath, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }

    private sealed class ThrowingFileStorage : IFileStorageService
    {
        public Task<StoredFileResult> SaveObjectAsync(
            Stream fileStream, string folder, string contentType, string extension, CancellationToken cancellationToken = default)
            => throw new FileStorageException("R2 is unreachable in this test.");

        public Task<string> SaveFileAsync(Stream fileStream, string fileName, string folder = "products", CancellationToken cancellationToken = default)
            => throw new FileStorageException("R2 is unreachable in this test.");

        public Task<bool> DeleteFileAsync(string relativePath, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }

    private static OrderService CreateOrderService(AadhiDbContext context, IFileStorageService storage)
    {
        var user = new TestCurrentUser();
        var outbox = new OutboxService(context);
        var audit = new AuditLogService(context, user, outbox);
        var numberGen = new BusinessNumberGenerator(context);
        return new OrderService(context, user, audit, outbox, numberGen, storage);
    }

    private static async Task<Product> SeedProductAsync(AadhiDbContext context)
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
            IsActive = true
        };
        context.Products.Add(product);

        await context.SaveChangesAsync();
        return product;
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
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private sealed class TestCurrentUser : ICurrentUserService
    {
        public string? UserId => "test-user-id";
        public string? Email => "admin@aadhicracker.in";
        public string? UserName => "admin";
        public string? Role => "SuperAdmin";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "TestRunner";
        public string CorrelationId => Guid.NewGuid().ToString();
        public bool IsAuthenticated => true;
    }
}
