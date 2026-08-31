using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AadhiCrackers.Api.Middleware;

public static class RateLimitingPolicies
{
    public const string PublicGeneral = "PUBLIC_GENERAL";
    public const string Login = "LOGIN";
    public const string PasswordReset = "PASSWORD_RESET";
    public const string ProductSearch = "PRODUCT_SEARCH";
    public const string Cart = "CART";
    public const string Checkout = "CHECKOUT";
    public const string OrderCreate = "ORDER_CREATE";
    public const string AdminApi = "ADMIN_API";
    public const string Reports = "REPORTS";
    public const string AuditSearch = "AUDIT_SEARCH";

    public static IServiceCollection AddAppRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.OnRejected = async (context, token) =>
            {
                var correlationId = context.HttpContext.Items["CorrelationId"]?.ToString() ?? Guid.NewGuid().ToString("N");
                var ip = context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
                var endpoint = context.HttpContext.Request.Path;

                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.Headers.Append("Retry-After", "60");

                var problem = new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Rate Limit Exceeded",
                    Detail = "Too many requests. Please slow down and try again after a brief pause.",
                    Instance = endpoint,
                    Type = "https://httpstatuses.com/429"
                };
                problem.Extensions["correlationId"] = correlationId;
                problem.Extensions["retryAfterSeconds"] = 60;
                problem.Extensions["clientIp"] = ip;

                await context.HttpContext.Response.WriteAsJsonAsync(problem, cancellationToken: token);
            };

            // 1. PUBLIC GENERAL: 100 req / min / IP
            options.AddPolicy(PublicGeneral, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "anon",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 100,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            // 2. LOGIN: 5 req / min / IP
            options.AddPolicy(Login, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "anon",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));

            // 3. PASSWORD RESET: 3 req / 15 min / IP
            options.AddPolicy(PasswordReset, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "anon",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 3,
                        Window = TimeSpan.FromMinutes(15),
                        QueueLimit = 0
                    }));

            // 4. PRODUCT SEARCH: 60 req / min / IP
            options.AddPolicy(ProductSearch, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "anon",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 60,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));

            // 5. CART: 60 req / min
            options.AddPolicy(Cart, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.User?.Identity?.Name ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "anon",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 60,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));

            // 6. CHECKOUT: 10 req / min
            options.AddPolicy(Checkout, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.User?.Identity?.Name ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "anon",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));

            // 7. ORDER CREATE: 10 req / 10 min
            options.AddPolicy(OrderCreate, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.User?.Identity?.Name ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "anon",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(10),
                        QueueLimit = 0
                    }));

            // 8. ADMIN API: 120 req / min
            options.AddPolicy(AdminApi, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.User?.Identity?.Name ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "anon",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 120,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));

            // 9. REPORTS: 30 req / min
            options.AddPolicy(Reports, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.User?.Identity?.Name ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "anon",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 30,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));

            // 10. AUDIT SEARCH: 60 req / min
            options.AddPolicy(AuditSearch, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.User?.Identity?.Name ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "anon",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 60,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));
        });

        return services;
    }
}
