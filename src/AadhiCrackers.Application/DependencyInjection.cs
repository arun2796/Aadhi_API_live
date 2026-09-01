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
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<IPurchaseService, PurchaseService>();
        services.AddScoped<IFinanceService, FinanceService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<ISettingsService, SettingsService>();
        services.AddScoped<ICartService, CartService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IPromotionService, PromotionService>();
        services.AddScoped<IQuoteService, QuoteService>();
        services.AddScoped<ISystemHealthService, SystemHealthService>();

        return services;
    }
}
