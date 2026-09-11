using System.Text.Json;
using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Application.Services;
using AadhiCrackers.Contracts.Catalog;
using AadhiCrackers.Contracts.Common;
using AadhiCrackers.Domain.Entities;
using AadhiCrackers.Domain.Enums;
using AadhiCrackers.Infrastructure.Persistence;
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

/// <summary>
/// The default provider, and the only one used for local development: files go under
/// wwwroot/storage and are served back by UseStaticFiles at /storage/...
///
/// NOT SUITABLE FOR PRODUCTION. Uploads land inside the deployed release directory, which the
/// next deploy replaces, so they are gone. Production sets Storage:Provider=R2 (see
/// <see cref="R2FileStorageService"/>); this class stays the default purely so a developer with no
/// Cloudflare credentials can still run the whole upload flow end to end.
/// </summary>
public class LocalFileStorageService : IFileStorageService
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".avif", ".pdf", ".gif"
    };

    private readonly string _baseStoragePath;

    public LocalFileStorageService()
    {
        _baseStoragePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "storage");
        Directory.CreateDirectory(_baseStoragePath);
    }

    /// <summary>
    /// Mirrors <see cref="R2FileStorageService.SaveObjectAsync"/> so the upload endpoint behaves
    /// identically under either provider. The content type is accepted and echoed back rather than
    /// stored: on disk it is the extension that decides what UseStaticFiles serves, and the
    /// extension here was itself derived from the sniffed content type.
    /// </summary>
    public async Task<StoredFileResult> SaveObjectAsync(
        Stream fileStream,
        string folder,
        string contentType,
        string extension,
        CancellationToken cancellationToken = default)
    {
        var key = StorageKeyGenerator.NewKey(folder, extension);
        var targetFolder = Path.Combine(_baseStoragePath, StorageKeyGenerator.SanitizeFolder(folder));
        Directory.CreateDirectory(targetFolder);

        var fullPath = Path.Combine(_baseStoragePath, key.Replace('/', Path.DirectorySeparatorChar));

        long sizeBytes;
        await using (var outputStream = new FileStream(fullPath, FileMode.Create))
        {
            await fileStream.CopyToAsync(outputStream, cancellationToken);
            sizeBytes = outputStream.Length;
        }

        return new StoredFileResult($"/storage/{key}", key, contentType, sizeBytes);
    }

    public async Task<string> SaveFileAsync(Stream fileStream, string fileName, string folder = "products", CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.Contains(extension))
        {
            throw new ArgumentException($"File extension '{extension}' is not permitted. Allowed extensions: {string.Join(", ", AllowedExtensions)}");
        }

        var result = await SaveObjectAsync(fileStream, folder, StorageContentTypes.FromExtension(extension), extension, cancellationToken);
        return result.Url;
    }

    /// <summary>
    /// Accepts either the URL form ("/storage/products/ab12.png") or the bare key
    /// ("products/ab12.png") that <see cref="SaveObjectAsync"/> returns, so callers holding either
    /// value can delete. Resolution is confined to wwwroot: a traversing path deletes nothing.
    /// </summary>
    public Task<bool> DeleteFileAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var value = (relativePath ?? string.Empty).Trim().Replace('\\', '/').TrimStart('/');
        if (value.Length == 0) return Task.FromResult(false);

        // A key produced by SaveObjectAsync is relative to the storage root, not to wwwroot.
        if (!value.StartsWith("storage/", StringComparison.OrdinalIgnoreCase))
        {
            value = "storage/" + value;
        }

        var webRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"));
        var fullPath = Path.GetFullPath(Path.Combine(webRoot, value));

        if (!fullPath.StartsWith(webRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(false);
        }

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }
}

/// <summary>
/// Writes customer-facing notifications into OUR OWN Notifications table and keeps the existing
/// structured logging. There is deliberately no SMS gateway, no WhatsApp API and no e-mail
/// provider: the shop stores what happened, the storefront reads it back over
/// api/v1/notifications.
///
/// It is driven entirely by the outbox (OutboxProcessorBackgroundService), which already resolves
/// every order lifecycle event to a call on this interface, so no controller has to invoke
/// anything new.
///
/// IDEMPOTENCY. The outbox retries and can redeliver, so every row is keyed by the LOGICAL event
/// (Notification.DedupeKey, unique-indexed) rather than the delivery attempt: a redelivery finds
/// the key already present and does nothing.
///
/// GUEST ISOLATION. Every anonymous checkout is attached to one shared customer record
/// (guest@aadhicracker.in). Attributing a guest notification to that record would make it visible
/// to every other guest through "my notifications", so a notification whose order belongs to the
/// shared bucket is stored with CustomerId = NULL and is reachable only by its order number.
/// Resolution fails closed: if the owning customer cannot be read, no owner is recorded.
/// </summary>
public class NotificationService : INotificationService
{
    private const int MaxTitleLength = 150;
    private const int MaxMessageLength = 1000;

    private readonly ILogger<NotificationService> _logger;
    private readonly AadhiDbContext _context;

    public NotificationService(ILogger<NotificationService> logger, AadhiDbContext context)
    {
        _logger = logger;
        _context = context;
    }

    public async Task SendOrderConfirmationAsync(Order order, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("📧 [Notification Service] Order confirmation dispatched for Order #{OrderNumber}, Total: {GrandTotal}",
            order.OrderNumber, order.GrandTotal.Format());

        var notification = await BuildForOrderAsync(order, NotificationType.OrderPlaced, $"OrderPlaced|{order.Id}", cancellationToken);
        notification.Title = $"Order {order.OrderNumber} placed";
        notification.Message =
            $"We have received your order {order.OrderNumber} for {order.GrandTotal.Format()}. " +
            "We will confirm it as soon as your payment is verified, and we will tell you here the moment it is handed to the transport company.";
        notification.DataJson = SerializeData(new Dictionary<string, string>
        {
            ["grandTotal"] = order.GrandTotal.Format(),
            ["grandTotalAmount"] = order.GrandTotal.ToDecimal().ToString(System.Globalization.CultureInfo.InvariantCulture)
        });

        await PersistAsync(notification, cancellationToken);
    }

    public async Task SendOrderStatusUpdatedAsync(Order order, string? newStatus = null, CancellationToken cancellationToken = default)
    {
        // The event's own NewStatus is preferred over the order row: several status events can be
        // waiting in the outbox at once and the row already shows the LATEST status, which would
        // collapse two distinct notifications into one.
        var status = string.IsNullOrWhiteSpace(newStatus) ? order.OrderStatus.ToString() : newStatus.Trim();

        _logger.LogInformation("📧 [Notification Service] Order status update notification dispatched for Order #{OrderNumber}, New Status: {Status}",
            order.OrderNumber, status);

        var notification = await BuildForOrderAsync(order, NotificationType.OrderStatusChanged, $"OrderStatusChanged|{order.Id}|{status}", cancellationToken);
        notification.OrderStatus = status;
        notification.Title = BuildStatusTitle(order.OrderNumber, status);
        notification.Message = BuildStatusMessage(order, status);

        await PersistAsync(notification, cancellationToken);
    }

    public async Task SendPaymentVerifiedAsync(Order order, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("📧 [Notification Service] Payment verified notification dispatched for Order #{OrderNumber}, Total: {GrandTotal}",
            order.OrderNumber, order.GrandTotal.Format());

        var notification = await BuildForOrderAsync(order, NotificationType.PaymentVerified, $"PaymentVerified|{order.Id}", cancellationToken);
        notification.Title = $"Payment verified for order {order.OrderNumber}";
        notification.Message =
            $"Your payment of {order.GrandTotal.Format()} for order {order.OrderNumber} has been verified. " +
            "Your order is confirmed and is being prepared for dispatch.";
        notification.DataJson = SerializeData(new Dictionary<string, string>
        {
            ["grandTotal"] = order.GrandTotal.Format(),
            ["utrNumber"] = order.UtrNumber ?? string.Empty
        });

        await PersistAsync(notification, cancellationToken);
    }

    public async Task SendPaymentRejectedAsync(Order order, string reason, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("📧 [Notification Service] Payment rejected notification dispatched for Order #{OrderNumber}. Reason: {Reason}",
            order.OrderNumber, reason);

        var notification = await BuildForOrderAsync(order, NotificationType.PaymentRejected, $"PaymentRejected|{order.Id}", cancellationToken);
        notification.Title = $"Payment could not be verified for order {order.OrderNumber}";
        notification.Message =
            $"We could not verify the payment for order {order.OrderNumber}, so the order has been cancelled. " +
            $"Reason: {reason}. Please contact us with the correct payment reference and we will help you place it again.";
        notification.DataJson = SerializeData(new Dictionary<string, string>
        {
            ["reason"] = reason,
            ["utrNumber"] = order.UtrNumber ?? string.Empty
        });

        await PersistAsync(notification, cancellationToken);
    }

    /// <summary>
    /// THE notification this whole feature exists for. The consignment goes by lorry to a transport
    /// office and the customer collects it there, so the message must name the transport company,
    /// the LR / waybill number to quote, and the office phone number and address to walk into. Those
    /// four facts are stored as columns as well as inside the rendered message, so the app can make
    /// the phone tappable and the LR copyable without parsing the sentence.
    /// </summary>
    public async Task SendOrderDispatchedAsync(Order order, string carrierName, string trackingNumber, string? carrierPhone, string? carrierAddress, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "📦 [Notification Service] Dispatch notification dispatched for Order #{OrderNumber}: carrier {CarrierName}, LR/waybill {TrackingNumber}, transport office phone {CarrierPhone}, address {CarrierAddress}",
            order.OrderNumber, carrierName, trackingNumber, carrierPhone ?? "(not recorded)", carrierAddress ?? "(not recorded)");

        var notification = await BuildForOrderAsync(order, NotificationType.OrderDispatched, $"OrderDispatched|{order.Id}|{trackingNumber}", cancellationToken);
        notification.OrderStatus = OrderStatus.Shipped.ToString();
        notification.CarrierName = carrierName;
        notification.TrackingNumber = trackingNumber;
        notification.CarrierPhone = string.IsNullOrWhiteSpace(carrierPhone) ? null : carrierPhone.Trim();
        notification.CarrierAddress = string.IsNullOrWhiteSpace(carrierAddress) ? null : carrierAddress.Trim();

        notification.Title = $"Order {order.OrderNumber} dispatched via {carrierName}";

        var message = new System.Text.StringBuilder();
        message.Append($"Your order {order.OrderNumber} has been dispatched through {carrierName}. ");
        message.Append($"LR / waybill number: {trackingNumber}. ");
        message.Append("Collect your parcel from the transport office");
        if (notification.CarrierAddress != null)
        {
            message.Append($" at {notification.CarrierAddress}");
        }
        message.Append('.');
        if (notification.CarrierPhone != null)
        {
            message.Append($" Transport office phone: {notification.CarrierPhone}.");
        }
        message.Append($" Please quote LR / waybill {trackingNumber} and carry a photo ID when you collect it.");
        notification.Message = message.ToString();

        await PersistAsync(notification, cancellationToken);
    }

    public Task SendLowStockAlertAsync(Product product, int currentStock, CancellationToken cancellationToken = default)
    {
        // Operational, not customer-facing: the Notifications table is the customer's inbox, so a
        // low-stock alert deliberately stays a log line and is not stored there.
        _logger.LogWarning("⚠️ [Notification Service] Low stock alert: Product '{Name}' (SKU: {SKU}) is at {Stock} units (Reorder Level: {ReorderLevel})",
            product.Name, product.SKU, currentStock, product.ReorderLevel);
        return Task.CompletedTask;
    }

    public Task SendPasswordResetOtpAsync(string recipient, string otpCode, CancellationToken cancellationToken = default)
    {
        // Logging stub — replace with SMS/email gateway integration in production.
        //
        // The OTP is a credential and is NEVER returned in an HTTP response, and never written to
        // the Notifications table either: those rows are served back over HTTP, so storing an OTP
        // there would hand the code to anyone who can read the inbox. Developers who need it
        // locally read it from this log line, which is compiled in ONLY for DEBUG builds: a Release
        // build physically does not contain the code that prints it, so no environment variable,
        // appsettings value or log-level change can turn the leak on in a deployed instance.
#if DEBUG
        _logger.LogInformation("📧 [Notification Service] Password reset OTP {OtpCode} dispatched to {Recipient} (valid for 5 minutes)",
            otpCode, recipient);
#else
        _logger.LogInformation("📧 [Notification Service] Password reset OTP dispatched to {Recipient} (valid for 5 minutes)",
            recipient);
#endif
        return Task.CompletedTask;
    }

    // ---------------------------------------------------------------------------------------
    // Persistence plumbing
    // ---------------------------------------------------------------------------------------

    private async Task<Notification> BuildForOrderAsync(Order order, NotificationType type, string dedupeKey, CancellationToken cancellationToken)
    {
        return new Notification
        {
            CustomerId = await ResolveOwnerCustomerIdAsync(order, cancellationToken),
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            Type = type,
            OrderStatus = order.OrderStatus.ToString(),
            DedupeKey = dedupeKey,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    /// <summary>
    /// The customer this notification may be served to under "my notifications", or NULL when there
    /// is no such single owner. Guest checkouts all share one customer record, so that record is
    /// never treated as an owner; the row is then reachable only through its order number, which is
    /// exactly the handle a guest has. Fails closed: an unreadable customer yields NULL.
    /// </summary>
    private async Task<Guid?> ResolveOwnerCustomerIdAsync(Order order, CancellationToken cancellationToken)
    {
        var email = order.Customer?.Email;

        if (string.IsNullOrWhiteSpace(email))
        {
            email = await _context.Customers
                .AsNoTracking()
                .Where(c => c.Id == order.CustomerId)
                .Select(c => c.Email)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(email) || OrderPricingService.IsSharedGuestBucket(email))
        {
            return null;
        }

        return order.CustomerId;
    }

    private async Task PersistAsync(Notification notification, CancellationToken cancellationToken)
    {
        notification.Title = Truncate(notification.Title, MaxTitleLength);
        notification.Message = Truncate(notification.Message, MaxMessageLength);

        var alreadyStored = await _context.Notifications
            .AsNoTracking()
            .AnyAsync(n => n.DedupeKey == notification.DedupeKey, cancellationToken);

        if (alreadyStored)
        {
            _logger.LogDebug("Notification {DedupeKey} already stored; redelivery ignored.", notification.DedupeKey);
            return;
        }

        _context.Notifications.Add(notification);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Stored {Type} notification {Id} for order {OrderNumber} (customer {CustomerId}).",
                notification.Type, notification.Id, notification.OrderNumber, notification.CustomerId?.ToString() ?? "guest / none");
        }
        catch (DbUpdateException ex)
        {
            // Lost a race against a concurrent delivery of the same logical event: the unique index
            // on DedupeKey did its job. Drop the duplicate so the caller's own SaveChanges (the
            // outbox marking the message processed) is not poisoned by a doomed pending insert.
            _context.Entry(notification).State = EntityState.Detached;

            if (await _context.Notifications.AsNoTracking().AnyAsync(n => n.DedupeKey == notification.DedupeKey, cancellationToken))
            {
                _logger.LogWarning(ex, "Notification {DedupeKey} was stored concurrently; duplicate discarded.", notification.DedupeKey);
                return;
            }

            throw;
        }
    }

    private static string BuildStatusTitle(string orderNumber, string status) => status switch
    {
        nameof(OrderStatus.Confirmed) => $"Order {orderNumber} confirmed",
        nameof(OrderStatus.Processing) => $"Order {orderNumber} is being prepared",
        nameof(OrderStatus.Packed) => $"Order {orderNumber} is packed",
        nameof(OrderStatus.Shipped) => $"Order {orderNumber} has been shipped",
        nameof(OrderStatus.OutForDelivery) => $"Order {orderNumber} is out for delivery",
        nameof(OrderStatus.Delivered) => $"Order {orderNumber} delivered",
        nameof(OrderStatus.Cancelled) => $"Order {orderNumber} cancelled",
        nameof(OrderStatus.Returned) => $"Order {orderNumber} returned",
        _ => $"Order {orderNumber} updated"
    };

    private static string BuildStatusMessage(Order order, string status)
    {
        var orderNumber = order.OrderNumber;

        if (status == nameof(OrderStatus.Shipped))
        {
            // An admin can move an order to Shipped without going through the dispatch desk; if the
            // carrier details are on the order anyway, repeat them here - they are what the customer
            // actually needs in order to collect the parcel.
            var shipped = $"Your order {orderNumber} has been handed to the transport company.";
            if (!string.IsNullOrWhiteSpace(order.CarrierName))
            {
                shipped += $" Transport company: {order.CarrierName}.";
            }
            if (!string.IsNullOrWhiteSpace(order.TrackingNumber))
            {
                shipped += $" LR / waybill number: {order.TrackingNumber}.";
            }
            if (!string.IsNullOrWhiteSpace(order.CarrierPhone))
            {
                shipped += $" Transport office phone: {order.CarrierPhone}.";
            }
            if (!string.IsNullOrWhiteSpace(order.CarrierAddress))
            {
                shipped += $" Collect it from {order.CarrierAddress}.";
            }
            return shipped;
        }

        return status switch
        {
            nameof(OrderStatus.Confirmed) => $"Your order {orderNumber} is confirmed. We are getting it ready for packing.",
            nameof(OrderStatus.Processing) => $"Your order {orderNumber} is being prepared.",
            nameof(OrderStatus.Packed) => $"Your order {orderNumber} has been packed in fire-safe cartons and is waiting to be handed to the transport company.",
            nameof(OrderStatus.OutForDelivery) => $"Your order {orderNumber} is out for delivery.",
            nameof(OrderStatus.Delivered) => $"Your order {orderNumber} has been delivered. Thank you for shopping with Aadhi Crackers.",
            nameof(OrderStatus.Cancelled) => $"Your order {orderNumber} has been cancelled.",
            nameof(OrderStatus.Returned) => $"Your order {orderNumber} has been marked as returned.",
            _ => $"The status of your order {orderNumber} is now {status}."
        };
    }

    private static string? SerializeData(Dictionary<string, string> data)
    {
        var cleaned = data
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        return cleaned.Count == 0 ? null : JsonSerializer.Serialize(cleaned);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…";
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
                IsGiftBox = p.IsGiftBox,
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
