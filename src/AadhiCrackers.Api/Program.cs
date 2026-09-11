using AadhiCrackers.Api.Middleware;
using AadhiCrackers.Api.Serialization;
using AadhiCrackers.Api.Services;
using AadhiCrackers.Application;
using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Infrastructure;
using AadhiCrackers.Infrastructure.Configuration;
using AadhiCrackers.Infrastructure.Identity;
using AadhiCrackers.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

// 1. Serilog Configuration
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "AadhiCrackers.Api")
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{CorrelationId}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/aadhi-.txt", rollingInterval: RollingInterval.Day, outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{CorrelationId}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

builder.Host.UseSerilog();

// 2. Add Services from Application & Infrastructure Layers
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);

// 3. API & Web Services
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddControllers(options =>
    {
        options.Filters.Add<FluentValidationActionFilter>();
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());

        // Every timestamp column is PostgreSQL "timestamp with time zone", and Npgsql rejects a
        // DateTime whose Kind is not Utc. A JSON date without a trailing "Z" deserializes as
        // Unspecified, so without these converters such a request would blow up at SaveChanges
        // with an error that names neither the field nor the request.
        options.JsonSerializerOptions.Converters.Add(new UtcDateTimeJsonConverter());
        options.JsonSerializerOptions.Converters.Add(new NullableUtcDateTimeJsonConverter());
    });

builder.Services.AddAppRateLimiting();

// 4. CORS
// Origins come from config key "Cors:AllowedOrigins" (env Cors__AllowedOrigins) as a semicolon-
// or comma-separated list, e.g. "https://aadhicracker.in;https://adminerp.aadhicracker.in".
// When configured, CORS is restricted to exactly those origins; when empty, the historical
// allow-all behavior is kept (and a warning is logged outside Development).
var corsAllowedOrigins = (builder.Configuration["Cors:AllowedOrigins"] ?? string.Empty)
    .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Select(origin => origin.TrimEnd('/'))
    .Where(origin => origin.Length > 0)
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AadhiCorsPolicy", policy =>
    {
        if (corsAllowedOrigins.Length > 0)
        {
            policy.WithOrigins(corsAllowedOrigins);
        }
        else
        {
            policy.SetIsOriginAllowed(_ => true); // Allow localhost, LAN IPs, and frontend origins
        }

        policy.AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()
            .WithExposedHeaders("X-Correlation-ID", "Retry-After");
    });
});

// 5. Health Checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AadhiDbContext>("PostgreSQL Database", tags: new[] { "ready", "db" });

// 6. OpenAPI / Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "AADHI CRACKERS API",
        Version = "v1",
        Description = "Production-grade API for AADHI CRACKERS E-Commerce & ERP platform",
        Contact = new OpenApiContact
        {
            Name = "AADHI CRACKERS Team",
            Email = "support@aadhicracker.in"
        }
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// 7. Production configuration checks
if (!app.Environment.IsDevelopment())
{
    // REFUSING TO START IS THE POINT. The fallback secret is a constant in this repository, so
    // anyone who can read the source can mint a valid SuperAdmin token against a deployment that
    // still uses it. A warning in a log nobody reads is not a defence — this is the same policy
    // already applied to a missing database connection string and missing R2 credentials.
    if (JwtSecretProvider.IsDefaultSecret(app.Configuration))
    {
        throw new InvalidOperationException(
            "JwtSettings:SecretKey is unset, so the built-in default signing key would be used. " +
            "That key is public (it is a constant in this repository) and anyone holding it can forge " +
            "an admin token. Set JwtSettings__SecretKey in the environment — generate one with " +
            "'openssl rand -base64 48'. Changing it signs everyone out, which is expected.");
    }

    // HS256 keys shorter than the 256-bit hash add no security beyond their own length.
    var configuredSecret = JwtSecretProvider.Resolve(app.Configuration);
    if (System.Text.Encoding.UTF8.GetByteCount(configuredSecret) < 32)
    {
        throw new InvalidOperationException(
            $"JwtSettings:SecretKey is only {System.Text.Encoding.UTF8.GetByteCount(configuredSecret)} bytes. " +
            "HS256 needs at least 32. Generate one with 'openssl rand -base64 48'.");
    }

    // CORS stays a warning: allow-all is bad practice but, unlike a forgeable token, it does not
    // by itself hand anyone an admin session — and blocking startup over it could take the shop
    // offline for a misconfigured origin during a launch.
    if (corsAllowedOrigins.Length == 0)
    {
        app.Logger.LogWarning(
            "⚠️ SECURITY WARNING: CORS is allowing all origins — set Cors__AllowedOrigins " +
            "(e.g. \"https://aadhicracker.in;https://adminerp.aadhicracker.in\") to restrict access");
    }
}

if (corsAllowedOrigins.Length > 0)
{
    app.Logger.LogInformation("CORS restricted to configured origins: {Origins}", string.Join(", ", corsAllowedOrigins));
}

// State the image storage provider in the boot log. "Which provider is this deploy actually
// using?" is the first question when uploaded product images go missing, and it should be
// answerable from the logs without reproducing anything. Only non-secret values are printed.
var storageOptions = app.Services.GetRequiredService<StorageOptions>();
if (storageOptions.UsesR2)
{
    app.Logger.LogInformation(
        "🗄️ Image storage: Cloudflare R2 — bucket '{Bucket}', endpoint {Endpoint}, public URLs under {PublicBaseUrl}",
        storageOptions.R2.BucketName, storageOptions.R2.ServiceUrl, storageOptions.R2.PublicBaseUrl);
}
else
{
    app.Logger.LogInformation("🗄️ Image storage: local disk (wwwroot/storage). Set Storage__Provider=R2 for durable hosting.");

    if (!app.Environment.IsDevelopment())
    {
        app.Logger.LogWarning(
            "⚠️ Uploaded images are being written to the server's local disk and WILL BE LOST on the next deploy. " +
            "Set Storage__Provider=R2 plus Storage__R2__AccountId / AccessKeyId / SecretAccessKey / BucketName / PublicBaseUrl.");
    }
}

// 8. Seed Database and Initial Provisioning
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    try
    {
        var context = services.GetRequiredService<AadhiDbContext>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = services.GetRequiredService<RoleManager<ApplicationRole>>();

        await DatabaseInitializer.InitializeAsync(context, logger);
        await DatabaseSeeder.SeedAsync(
            context,
            userManager,
            roleManager,
            logger,
            app.Configuration,
            app.Environment.IsDevelopment());
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "An error occurred while initializing the database.");
        throw;
    }
}

// 9. HTTP Middleware Pipeline
app.UseExceptionHandler();

app.UseCorrelationId();

app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "AADHI CRACKERS API v1");
        c.RoutePrefix = "swagger";
    });
}

// Uploaded objects get the same one-year immutable cache header the R2 provider writes, so the
// local provider is a faithful rehearsal of production rather than a subtly different one. This is
// only safe because upload keys are random: a replaced image is a new key and therefore a new URL,
// so a cached copy can never be stale. Everything else under wwwroot keeps the default handling.
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        if (ctx.Context.Request.Path.StartsWithSegments("/storage", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        }
    }
});

app.UseRouting();

app.UseCors("AadhiCorsPolicy");

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Health check endpoints
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => true
});

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = r => r.Tags.Contains("live")
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = r => r.Tags.Contains("ready")
});

app.Run();
