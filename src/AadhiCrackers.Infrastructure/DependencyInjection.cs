using System.Text;
using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Domain.Entities;
using AadhiCrackers.Infrastructure.BackgroundJobs;
using AadhiCrackers.Infrastructure.Identity;
using AadhiCrackers.Infrastructure.Persistence;
using AadhiCrackers.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace AadhiCrackers.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? configuration.GetConnectionString("PostgreSqlConnection")
            ?? "Data Source=aadhicrackers.db";

        var provider = configuration["DatabaseProvider"] ?? "Sqlite";

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

        // JWT Authentication Configuration
        var secretKey = configuration["JwtSettings:SecretKey"];
        if (string.IsNullOrWhiteSpace(secretKey))
        {
            secretKey = "AadhiCrackers_Secure_Enterprise_JWT_Secret_Key_2026_!@#$999_SUPER_SECURE";
        }
        var issuer = configuration["JwtSettings:Issuer"] ?? "AadhiCrackers.Api";
        var audience = configuration["JwtSettings:Audience"] ?? "AadhiCrackers.Clients";

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.RequireHttpsMetadata = false;
            options.SaveToken = true;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
                ValidateIssuer = true,
                ValidIssuer = issuer,
                ValidateAudience = true,
                ValidAudience = audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };
        });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("RequireAdmin", policy => policy.RequireRole(AppRoles.SuperAdmin, AppRoles.Admin));
            options.AddPolicy("RequireSuperAdmin", policy => policy.RequireRole(AppRoles.SuperAdmin));
            options.AddPolicy("RequireInventoryManager", policy => policy.RequireRole(AppRoles.SuperAdmin, AppRoles.Admin, AppRoles.Manager, AppRoles.InventoryManager));
            options.AddPolicy("RequirePurchaseManager", policy => policy.RequireRole(AppRoles.SuperAdmin, AppRoles.Admin, AppRoles.Manager, AppRoles.PurchaseManager));
            options.AddPolicy("RequireAccountant", policy => policy.RequireRole(AppRoles.SuperAdmin, AppRoles.Admin, AppRoles.Manager, AppRoles.Accountant));
            options.AddPolicy("RequireSalesExecutive", policy => policy.RequireRole(AppRoles.SuperAdmin, AppRoles.Admin, AppRoles.Manager, AppRoles.SalesExecutive));
            options.AddPolicy("RequireSupportAgent", policy => policy.RequireRole(AppRoles.SuperAdmin, AppRoles.Admin, AppRoles.Manager, AppRoles.SupportAgent));
            options.AddPolicy("RequireStaff", policy => policy.RequireRole(
                AppRoles.SuperAdmin, AppRoles.Admin, AppRoles.Manager,
                AppRoles.SalesExecutive, AppRoles.InventoryManager,
                AppRoles.PurchaseManager, AppRoles.Accountant, AppRoles.SupportAgent));
        });

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
