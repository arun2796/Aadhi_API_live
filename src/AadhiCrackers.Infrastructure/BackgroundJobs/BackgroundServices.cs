using System.Text.Json;
using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AadhiCrackers.Infrastructure.BackgroundJobs;

public class OutboxProcessorBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OutboxProcessorBackgroundService> _logger;

    public OutboxProcessorBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<OutboxProcessorBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🚀 Outbox Processor Background Service started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
                var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

                var now = DateTime.UtcNow;
                var pendingMessages = await context.OutboxMessages
                    .Where(m => (m.Status == "Pending" || m.Status == "Failed") &&
                                (m.NextAttemptAtUtc == null || m.NextAttemptAtUtc <= now) &&
                                m.RetryCount < 5)
                    .OrderBy(m => m.OccurredOnUtc)
                    .Take(20)
                    .ToListAsync(stoppingToken);

                if (pendingMessages.Count > 0)
                {
                    _logger.LogInformation("Processing {Count} pending outbox messages...", pendingMessages.Count);

                    foreach (var msg in pendingMessages)
                    {
                        try
                        {
                            msg.Status = "Processing";
                            _logger.LogInformation("Dispatching Outbox Event [{Id}] Type={Type}", msg.Id, msg.Type);

                            // Execute typed business handlers
                            if (!string.IsNullOrWhiteSpace(msg.PayloadJson))
                            {
                                switch (msg.Type)
                                {
                                    case "OrderPlaced":
                                        {
                                            using var doc = JsonDocument.Parse(msg.PayloadJson);
                                            if (doc.RootElement.TryGetProperty("OrderId", out var orderIdProp) &&
                                                Guid.TryParse(orderIdProp.GetString(), out var orderId))
                                            {
                                                var order = await context.Orders
                                                    .Include(o => o.Customer)
                                                    .Include(o => o.Items)
                                                    .FirstOrDefaultAsync(o => o.Id == orderId, stoppingToken);

                                                if (order != null)
                                                {
                                                    await notificationService.SendOrderConfirmationAsync(order, stoppingToken);
                                                }
                                            }
                                            break;
                                        }
                                    case "OrderStatusChanged":
                                        {
                                            using var doc = JsonDocument.Parse(msg.PayloadJson);
                                            if (doc.RootElement.TryGetProperty("OrderId", out var orderIdProp) &&
                                                Guid.TryParse(orderIdProp.GetString(), out var orderId))
                                            {
                                                var order = await context.Orders
                                                    .Include(o => o.Customer)
                                                    .FirstOrDefaultAsync(o => o.Id == orderId, stoppingToken);

                                                if (order != null)
                                                {
                                                    await notificationService.SendOrderStatusUpdatedAsync(order, stoppingToken);
                                                }
                                            }
                                            break;
                                        }
                                    case "PaymentVerified":
                                    case "PaymentReceived":
                                        {
                                            using var doc = JsonDocument.Parse(msg.PayloadJson);
                                            if (doc.RootElement.TryGetProperty("OrderId", out var orderIdProp) &&
                                                Guid.TryParse(orderIdProp.GetString(), out var orderId))
                                            {
                                                var order = await context.Orders
                                                    .Include(o => o.Customer)
                                                    .FirstOrDefaultAsync(o => o.Id == orderId, stoppingToken);

                                                if (order != null)
                                                {
                                                    await notificationService.SendPaymentVerifiedAsync(order, stoppingToken);
                                                }
                                            }
                                            break;
                                        }
                                    case "PaymentRejected":
                                        {
                                            using var doc = JsonDocument.Parse(msg.PayloadJson);
                                            var reason = doc.RootElement.TryGetProperty("Reason", out var rProp) ? rProp.GetString() ?? "Payment rejected" : "Payment rejected";
                                            if (doc.RootElement.TryGetProperty("OrderId", out var orderIdProp) &&
                                                Guid.TryParse(orderIdProp.GetString(), out var orderId))
                                            {
                                                var order = await context.Orders
                                                    .Include(o => o.Customer)
                                                    .FirstOrDefaultAsync(o => o.Id == orderId, stoppingToken);

                                                if (order != null)
                                                {
                                                    await notificationService.SendPaymentRejectedAsync(order, reason, stoppingToken);
                                                }
                                            }
                                            break;
                                        }
                                    case "OrderDispatched":
                                        {
                                            using var doc = JsonDocument.Parse(msg.PayloadJson);
                                            var root = doc.RootElement;

                                            // Carrier details are read from the event payload rather than the order row so a
                                            // replayed / dead-lettered message still reports the consignment it was raised for.
                                            // CarrierPhone / CarrierAddress were added to the payload later, so messages
                                            // enqueued before that change simply have no such property.
                                            static string? ReadString(JsonElement element, string propertyName) =>
                                                element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
                                                    ? prop.GetString()
                                                    : null;

                                            if (root.TryGetProperty("OrderId", out var orderIdProp) &&
                                                Guid.TryParse(orderIdProp.GetString(), out var orderId))
                                            {
                                                var order = await context.Orders
                                                    .Include(o => o.Customer)
                                                    .FirstOrDefaultAsync(o => o.Id == orderId, stoppingToken);

                                                if (order != null)
                                                {
                                                    await notificationService.SendOrderDispatchedAsync(
                                                        order,
                                                        ReadString(root, "CarrierName") ?? order.CarrierName ?? "Unknown carrier",
                                                        ReadString(root, "TrackingNumber") ?? order.TrackingNumber ?? "Unknown LR / waybill",
                                                        ReadString(root, "CarrierPhone") ?? order.CarrierPhone,
                                                        ReadString(root, "CarrierAddress") ?? order.CarrierAddress,
                                                        stoppingToken);
                                                }
                                            }
                                            break;
                                        }
                                    case "PaymentRecorded":
                                    case "AuditLogCreated":
                                        {
                                            _logger.LogInformation("Domain ledger outbox event [{Id}] Type={Type} successfully processed.", msg.Id, msg.Type);
                                            break;
                                        }
                                    default:
                                        throw new NotSupportedException($"Outbox event type '{msg.Type}' has no registered handler and cannot be processed.");
                                }
                            }

                            // Only mark processed after actual handler execution succeeds
                            msg.Status = "Processed";
                            msg.ProcessedOnUtc = DateTime.UtcNow;
                            msg.Error = null;
                        }
                        catch (Exception ex)
                        {
                            msg.RetryCount++;
                            msg.Error = ex.Message;
                            if (msg.RetryCount >= 5)
                            {
                                msg.Status = "DeadLetter";
                                _logger.LogError(ex, "Outbox message {Id} exceeded retry limit. Moved to DeadLetter.", msg.Id);
                            }
                            else
                            {
                                msg.Status = "Failed";
                                var delaySec = (int)Math.Pow(2, msg.RetryCount) * 5;
                                msg.NextAttemptAtUtc = DateTime.UtcNow.AddSeconds(delaySec);
                                _logger.LogWarning(ex, "Failed to process outbox message {Id}. Retry #{RetryCount} scheduled in {Seconds}s.", msg.Id, msg.RetryCount, delaySec);
                            }
                        }
                    }

                    await context.SaveChangesAsync(stoppingToken);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Error occurred in Outbox Processor Background Service.");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }

        _logger.LogInformation("Outbox Processor Background Service is stopping.");
    }
}

public class LowStockMonitorBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LowStockMonitorBackgroundService> _logger;
    private readonly HashSet<Guid> _alertedProductIds = new();

    public LowStockMonitorBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<LowStockMonitorBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🔍 Low Stock Monitor Background Service started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
                var notifier = scope.ServiceProvider.GetRequiredService<INotificationService>();

                var lowStockProducts = await context.Products
                    .Where(p => !p.IsDeleted && p.IsActive && (p.StockQuantity - p.ReservedQuantity) <= p.ReorderLevel)
                    .ToListAsync(stoppingToken);

                var currentLowIds = lowStockProducts.Select(p => p.Id).ToHashSet();

                // Clean up recovered products from the alerted state cache
                _alertedProductIds.RemoveWhere(id => !currentLowIds.Contains(id));

                foreach (var product in lowStockProducts)
                {
                    // Deduplicate alerts: only send when product enters low-stock state
                    if (!_alertedProductIds.Contains(product.Id))
                    {
                        await notifier.SendLowStockAlertAsync(product, product.StockQuantity - product.ReservedQuantity, stoppingToken);
                        _alertedProductIds.Add(product.Id);
                    }
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Error occurred in Low Stock Monitor Background Service.");
            }

            // Run check every 15 minutes
            await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
        }
    }
}
