using System.Text.Json;
using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Contracts.Catalog;
using AadhiCrackers.Contracts.Common;
using AadhiCrackers.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AadhiCrackers.Infrastructure.Services;

public class OutboxService : IOutboxService
{
    private readonly IApplicationDbContext _context;

    public OutboxService(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task EnqueueAsync(string type, object payload, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(payload);
        var message = new OutboxMessage
        {
            OccurredOnUtc = DateTime.UtcNow,
            Type = type,
            PayloadJson = json,
            RetryCount = 0
        };

        _context.OutboxMessages.Add(message);
        // Note: SaveChangesAsync will be called by the outer unit of work / service transaction
    }
}

public class LocalFileStorageService : IFileStorageService
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".pdf", ".gif"
    };

    private readonly string _baseStoragePath;

    public LocalFileStorageService()
    {
        _baseStoragePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "storage");
        Directory.CreateDirectory(_baseStoragePath);
    }

    public async Task<string> SaveFileAsync(Stream fileStream, string fileName, string folder = "products", CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.Contains(extension))
        {
            throw new ArgumentException($"File extension '{extension}' is not permitted. Allowed extensions: {string.Join(", ", AllowedExtensions)}");
        }

        var targetFolder = Path.Combine(_baseStoragePath, folder);
        Directory.CreateDirectory(targetFolder);

        var uniqueFileName = $"{Guid.NewGuid():N}{extension}";
        var fullPath = Path.Combine(targetFolder, uniqueFileName);

        using (var outputStream = new FileStream(fullPath, FileMode.Create))
        {
            await fileStream.CopyToAsync(outputStream, cancellationToken);
        }

        return $"/storage/{folder}/{uniqueFileName}";
    }

    public Task<bool> DeleteFileAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", relativePath.TrimStart('/'));
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }
}

public class NotificationService : INotificationService
{
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(ILogger<NotificationService> logger)
    {
        _logger = logger;
    }

    public Task SendOrderConfirmationAsync(Order order, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("📧 [Notification Service] Order confirmation dispatched for Order #{OrderNumber}, Total: {GrandTotal}",
            order.OrderNumber, order.GrandTotal.Format());
        return Task.CompletedTask;
    }

    public Task SendOrderStatusUpdatedAsync(Order order, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("📧 [Notification Service] Order status update notification dispatched for Order #{OrderNumber}, New Status: {Status}",
            order.OrderNumber, order.OrderStatus);
        return Task.CompletedTask;
    }

    public Task SendPaymentVerifiedAsync(Order order, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("📧 [Notification Service] Payment verified notification dispatched for Order #{OrderNumber}, Total: {GrandTotal}",
            order.OrderNumber, order.GrandTotal.Format());
        return Task.CompletedTask;
    }

    public Task SendPaymentRejectedAsync(Order order, string reason, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("📧 [Notification Service] Payment rejected notification dispatched for Order #{OrderNumber}. Reason: {Reason}",
            order.OrderNumber, reason);
        return Task.CompletedTask;
    }


    public Task SendLowStockAlertAsync(Product product, int currentStock, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("⚠️ [Notification Service] Low stock alert: Product '{Name}' (SKU: {SKU}) is at {Stock} units (Reorder Level: {ReorderLevel})",
            product.Name, product.SKU, currentStock, product.ReorderLevel);
        return Task.CompletedTask;
    }

    public Task SendPasswordResetOtpAsync(string recipient, string otpCode, CancellationToken cancellationToken = default)
    {
        // Logging stub — replace with SMS/email gateway integration in production.
        _logger.LogInformation("📧 [Notification Service] Password reset OTP {OtpCode} dispatched to {Recipient} (valid for 5 minutes)",
            otpCode, recipient);
        return Task.CompletedTask;
    }
}

public class SearchService : ISearchService
{
    private readonly IApplicationDbContext _context;

    public SearchService(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<ProductDto>> SearchProductsAsync(string query, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var q = query.Trim().ToLower();

        var dbQuery = _context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Images)
            .Include(p => p.Reviews)
            .Where(p => p.IsActive && !p.IsDeleted &&
                        (p.Name.ToLower().Contains(q) ||
                         p.SKU.ToLower().Contains(q) ||
                         p.Description.ToLower().Contains(q) ||
                         p.Category.Name.ToLower().Contains(q) ||
                         (p.Brand != null && p.Brand.Name.ToLower().Contains(q))));

        var totalCount = await dbQuery.CountAsync(cancellationToken);

        var items = await dbQuery
            .OrderByDescending(p => p.IsBestSeller)
            .ThenBy(p => p.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new ProductDto
            {
                Id = p.Id,
                SKU = p.SKU,
                Name = p.Name,
                Slug = p.Slug,
                ShortDescription = p.ShortDescription,
                CategoryId = p.CategoryId,
                CategoryName = p.Category.Name,
                BrandId = p.BrandId,
                BrandName = p.Brand != null ? p.Brand.Name : "AADHI CRACKERS",
                Price = p.Price.ToDecimal(),
                CompareAtPrice = p.CompareAtPrice != null ? p.CompareAtPrice.Value.ToDecimal() : null,
                StockQuantity = p.StockQuantity,
                AvailableQuantity = Math.Max(0, p.StockQuantity - p.ReservedQuantity),
                ReorderLevel = p.ReorderLevel,
                Unit = p.Unit,
                IsActive = p.IsActive,
                IsFeatured = p.IsFeatured,
                IsBestSeller = p.IsBestSeller,
                IsNewArrival = p.IsNewArrival,
                PrimaryImageUrl = p.Images.OrderBy(i => i.SortOrder).FirstOrDefault(i => i.IsPrimary) != null
                    ? p.Images.OrderBy(i => i.SortOrder).FirstOrDefault(i => i.IsPrimary)!.Url
                    : p.Images.OrderBy(i => i.SortOrder).FirstOrDefault() != null ? p.Images.OrderBy(i => i.SortOrder).FirstOrDefault()!.Url : null,
                Rating = p.Reviews.Any(r => r.Status == "Approved")
                    ? Math.Round(p.Reviews.Where(r => r.Status == "Approved").Average(r => (double)r.Rating), 1)
                    : 0,
                ReviewCount = p.Reviews.Count(r => r.Status == "Approved")
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<ProductDto>(items, totalCount, page, pageSize);
    }
}
