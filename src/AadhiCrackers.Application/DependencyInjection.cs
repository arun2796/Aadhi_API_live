using AadhiCrackers.Application.Services;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace AadhiCrackers.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddScoped<ICatalogService, CatalogService>();
        // The single order-money calculator shared by POST /cart/calculate and POST /orders.
        services.AddScoped<IOrderPricingService, OrderPricingService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IFinanceService, FinanceService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<ISettingsService, SettingsService>();
        services.AddScoped<ICartService, CartService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IPromotionService, PromotionService>();
        services.AddScoped<IReviewService, ReviewService>();
        services.AddScoped<IBannerService, BannerService>();
        services.AddScoped<ISystemHealthService, SystemHealthService>();
        services.AddScoped<IWishlistService, WishlistService>();
        services.AddScoped<IAddressService, AddressService>();
        // Read/flag side of the in-house notification inbox (rows are written by INotificationService).
        services.AddScoped<INotificationQueryService, NotificationQueryService>();

        return services;
    }
}
