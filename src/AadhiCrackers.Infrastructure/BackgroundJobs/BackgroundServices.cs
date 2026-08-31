using AadhiCrackers.Application.Common.Interfaces;
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

                var pendingMessages = await context.OutboxMessages
                    .Where(m => m.ProcessedOnUtc == null && m.RetryCount < 5)
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
                            // Asynchronous projection / indexing / webhook simulation
                            _logger.LogDebug("Dispatching Outbox Message [{Id}] Type={Type}", msg.Id, msg.Type);

                            msg.ProcessedOnUtc = DateTime.UtcNow;
                            msg.Error = null;
                        }
                        catch (Exception ex)
                        {
                            msg.RetryCount++;
                            msg.Error = ex.Message;
                            _logger.LogError(ex, "Failed to process outbox message {Id}, RetryCount={RetryCount}", msg.Id, msg.RetryCount);
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

                foreach (var product in lowStockProducts)
                {
                    await notifier.SendLowStockAlertAsync(product, product.StockQuantity - product.ReservedQuantity, stoppingToken);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Error occurred in Low Stock Monitor Background Service.");
            }

            // Run check every 30 minutes
            await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
        }
    }
}
