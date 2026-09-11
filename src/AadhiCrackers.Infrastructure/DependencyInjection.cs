using System.Text;
using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Domain.Entities;
using AadhiCrackers.Infrastructure.BackgroundJobs;
using AadhiCrackers.Infrastructure.Configuration;
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
        // PostgreSQL is the only supported provider. There is deliberately no fallback: a missing
        // connection string fails at startup naming the key, rather than silently starting against
        // some throwaway local database that would accept writes and then lose them.
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? configuration.GetConnectionString("PostgreSqlConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "No database connection string. Set ConnectionStrings:DefaultConnection " +
                "(env ConnectionStrings__DefaultConnection) to the PostgreSQL connection string, e.g. " +
                "'Host=localhost;Port=5432;Database=aadhicrackers;Username=aadhi;Password=...' or a " +
                "'postgresql://user:password@host:5432/aadhicrackers' URI.");
        }

        services.AddDbContext<AadhiDbContext>(options =>
        {
            // Migrations live in their own project (AadhiCrackers.Infrastructure.MigrationsPostgres)
            // so the schema history is versioned separately from the runtime model.
            // Keyword form ("Host=...;Port=...") is what the server uses, but a postgres(ql)://
            // URI is also accepted; Npgsql only understands keyword form, so it is normalized first.
            options.UseNpgsql(
                PostgresConnectionStringHelper.Normalize(connectionString),
                b => b.MigrationsAssembly("AadhiCrackers.Infrastructure.MigrationsPostgres"));
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
        // Secret resolution: Jwt:Secret (env Jwt__Secret) -> JwtSettings:SecretKey -> built-in default.
        // A startup warning is logged (Program.cs) when the default is in use outside Development.
        var secretKey = JwtSecretProvider.Resolve(configuration);
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
        services.AddFileStorage(configuration);
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<ISearchService, SearchService>();
        services.AddScoped<IOutboxService, OutboxService>();
        services.AddScoped<IBusinessNumberGenerator, BusinessNumberGenerator>();

        // Background Workers
        services.AddHostedService<OutboxProcessorBackgroundService>();
        services.AddHostedService<LowStockMonitorBackgroundService>();

        return services;
    }

    /// <summary>
    /// Picks the file storage provider from configuration.
    ///
    /// Storage:Provider (env Storage__Provider) selects it: "R2" -> Cloudflare R2, anything else
    /// unset or "Local" -> the historical local-disk provider, so an existing deployment that
    /// never sets the key keeps working exactly as before.
    ///
    /// WHY THIS THROWS. When the provider is R2 and any of the five R2 keys is missing, startup
    /// aborts with the key names in the message. Falling back to local disk would be far worse
    /// than crashing: each deploy replaces the release directory on the server, so the shop
    /// would upload 180 product images, see them work, and find them all broken the next morning
    /// with nothing in the logs to explain it.
    /// </summary>
    public static IServiceCollection AddFileStorage(this IServiceCollection services, IConfiguration configuration)
    {
        var storageOptions = StorageOptionsValidator.BindAndValidate(configuration);
        services.AddSingleton(storageOptions);

        if (storageOptions.UsesR2)
        {
            services.AddSingleton<IFileStorageService, R2FileStorageService>();
        }
        else
        {
            services.AddSingleton<IFileStorageService, LocalFileStorageService>();
        }

        return services;
    }
}
