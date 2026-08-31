using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Infrastructure.BackgroundJobs;
using AadhiCrackers.Infrastructure.Identity;
using AadhiCrackers.Infrastructure.Persistence;
using AadhiCrackers.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AadhiCrackers.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? "Data Source=aadhicrackers.db";

        services.AddDbContext<AadhiDbContext>(options =>
        {
            options.UseSqlite(connectionString, b => b.MigrationsAssembly(typeof(AadhiDbContext).Assembly.FullName));
        });

        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<AadhiDbContext>());

        // ASP.NET Core Identity
        services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
        {
            // Password policy
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = false;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequiredLength = 6;

            // Lockout policy
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.AllowedForNewUsers = true;

            // User settings
            options.User.RequireUniqueEmail = true;
        })
        .AddEntityFrameworkStores<AadhiDbContext>()
        .AddDefaultTokenProviders();

        // Infrastructure Services
        services.AddScoped<IIdentityService, IdentityService>();
        services.AddSingleton<IFileStorageService, LocalFileStorageService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<ISearchService, SearchService>();
        services.AddScoped<IOutboxService, OutboxService>();

        // Background Workers
        services.AddHostedService<OutboxProcessorBackgroundService>();
        services.AddHostedService<LowStockMonitorBackgroundService>();

        return services;
    }
}
