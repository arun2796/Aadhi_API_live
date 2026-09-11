using AadhiCrackers.Application.Services;
using AadhiCrackers.Contracts.Audit;
using AadhiCrackers.Contracts.Common;
using AadhiCrackers.Domain.Entities;
using AadhiCrackers.Domain.Enums;
using AadhiCrackers.Infrastructure.Configuration;
using AadhiCrackers.Infrastructure.Persistence;
using AadhiCrackers.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AadhiCrackers.Infrastructure.Tests;

/// <summary>
/// Covers the two ways image hosting can go quietly wrong in production: a misconfigured R2
/// provider silently writing to a container filesystem Render wipes on redeploy, and an R2 secret
/// leaking through the anonymous storefront settings endpoint.
/// </summary>
public class FileStorageConfigurationTests
{
    private static IConfiguration Config(params (string Key, string Value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(entries.Select(e => new KeyValuePair<string, string?>(e.Key, e.Value)))
            .Build();

    [Fact]
    public void NoStorageSection_DefaultsToLocal()
    {
        // An existing deployment that never sets the key must keep working exactly as before.
        var options = StorageOptionsValidator.BindAndValidate(Config());

        options.UsesLocal.Should().BeTrue();
        options.UsesR2.Should().BeFalse();
    }

    [Fact]
    public void BlankProvider_DefaultsToLocal()
    {
        var options = StorageOptionsValidator.BindAndValidate(Config(("Storage:Provider", "")));

        options.UsesLocal.Should().BeTrue();
    }

    [Fact]
    public void UnknownProvider_ThrowsRatherThanGuessing()
    {
        var act = () => StorageOptionsValidator.BindAndValidate(Config(("Storage:Provider", "S3")));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Storage:Provider*S3*");
    }

    [Fact]
    public void R2WithMissingSecret_FailsLoudlyAndNamesTheKey()
    {
        var act = () => StorageOptionsValidator.BindAndValidate(Config(
            ("Storage:Provider", "R2"),
            ("Storage:R2:AccountId", "abc123"),
            ("Storage:R2:AccessKeyId", "key"),
            ("Storage:R2:BucketName", "aadhi-images"),
            ("Storage:R2:PublicBaseUrl", "https://images.aadhicracker.in")));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Storage:R2:SecretAccessKey*")
            .WithMessage("*Storage__R2__SecretAccessKey*");
    }

    [Fact]
    public void R2WithSeveralMissingKeys_NamesAllOfThem()
    {
        var act = () => StorageOptionsValidator.BindAndValidate(Config(("Storage:Provider", "R2")));

        var thrown = act.Should().Throw<InvalidOperationException>().Which;
        foreach (var key in new[] { "AccountId", "AccessKeyId", "SecretAccessKey", "BucketName", "PublicBaseUrl" })
        {
            thrown.Message.Should().Contain($"Storage:R2:{key}");
        }
    }

    [Fact]
    public void R2WithNonAbsolutePublicBaseUrl_IsRejected()
    {
        var act = () => StorageOptionsValidator.BindAndValidate(Config(
            ("Storage:Provider", "R2"),
            ("Storage:R2:AccountId", "abc123"),
            ("Storage:R2:AccessKeyId", "key"),
            ("Storage:R2:SecretAccessKey", "secret"),
            ("Storage:R2:BucketName", "aadhi-images"),
            ("Storage:R2:PublicBaseUrl", "images.aadhicracker.in")));

        act.Should().Throw<InvalidOperationException>().WithMessage("*PublicBaseUrl*absolute*");
    }

    [Fact]
    public void ValidR2Configuration_BindsAndDerivesTheEndpoint()
    {
        var options = StorageOptionsValidator.BindAndValidate(Config(
            ("Storage:Provider", "r2"),
            ("Storage:R2:AccountId", "abc123"),
            ("Storage:R2:AccessKeyId", "key"),
            ("Storage:R2:SecretAccessKey", "secret"),
            ("Storage:R2:BucketName", "aadhi-images"),
            ("Storage:R2:PublicBaseUrl", "https://images.aadhicracker.in/")));

        options.UsesR2.Should().BeTrue();
        options.R2.ServiceUrl.Should().Be("https://abc123.r2.cloudflarestorage.com");

        // The trailing slash is trimmed so URL building never produces a double slash.
        options.R2.PublicBaseUrl.Should().Be("https://images.aadhicracker.in");
    }

    [Fact]
    public async Task PublicSettingsEndpoint_CannotLeakStorageCredentials()
    {
        // Two independent barriers: R2 values live only in IConfiguration and are never written to
        // SystemSettings, AND "Storage." is not on the public allow-list. This asserts the second
        // one, so that a future migration that DID seed such a row still would not expose it.
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<AadhiDbContext>().UseSqlite(connection).Options;
        await using var context = new AadhiDbContext(options);
        await context.Database.EnsureCreatedAsync();

        context.SystemSettings.AddRange(
            new SystemSetting { Key = "Storage.R2.SecretAccessKey", Value = "super-secret", Group = "Storage" },
            new SystemSetting { Key = "Storage.R2.AccessKeyId", Value = "an-access-key", Group = "Storage" },
            new SystemSetting { Key = "Store.BusinessName", Value = "AADHI CRACKERS", Group = "Store" });
        await context.SaveChangesAsync();

        var service = new SettingsService(context, new NoOpAuditLogService());
        var publicSettings = await service.GetPublicSettingsAsync();

        publicSettings.Should().ContainKey("Store.BusinessName");
        publicSettings.Keys.Should().NotContain(k => k.StartsWith("Storage.", StringComparison.OrdinalIgnoreCase));
        publicSettings.Values.Should().NotContain("super-secret");
    }

    [Fact]
    public async Task LocalProvider_StoresObjectAndReturnsMatchingUrlKeyAndSize()
    {
        var storage = new LocalFileStorageService();
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };
        using var stream = new MemoryStream(bytes);

        var result = await storage.SaveObjectAsync(stream, "products", "image/png", ".png");

        result.Key.Should().StartWith("products/").And.EndWith(".png");
        result.Url.Should().Be("/storage/" + result.Key);
        result.ContentType.Should().Be("image/png");
        result.SizeBytes.Should().Be(bytes.Length);

        // The key, not just the URL, must round-trip back to a delete.
        (await storage.DeleteFileAsync(result.Key)).Should().BeTrue();
        (await storage.DeleteFileAsync(result.Key)).Should().BeFalse();
    }

    [Theory]
    [InlineData("../../etc")]
    [InlineData("products/../../secrets")]
    [InlineData("")]
    public void GeneratedKeys_CannotEscapeTheirFolder(string folder)
    {
        var key = StorageKeyGenerator.NewKey(folder, ".png");

        key.Should().NotContain("..");
        key.Split('/').Should().HaveCount(2);
    }

    [Fact]
    public void GeneratedKeys_IgnoreTheUserSuppliedName()
    {
        var key = StorageKeyGenerator.NewKey("products", ".png");

        key.Should().MatchRegex("^products/[0-9a-f]{32}\\.png$");
    }

    private sealed class NoOpAuditLogService : IAuditLogService
    {
        public Task LogAsync(
            AuditAction action,
            string module,
            string entityType,
            string? entityId = null,
            string? entityName = null,
            object? before = null,
            object? after = null,
            object? changedFields = null,
            object? metadata = null,
            AuditSeverity severity = AuditSeverity.Info,
            bool success = true,
            string? failureReason = null,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<PagedResult<AuditLogDto>> GetAuditLogsAsync(AuditLogFilterRequest filter, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PagedResult<AuditLogDto>());

        public Task<AuditLogDetailDto?> GetAuditLogByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<AuditLogDetailDto?>(null);
    }
}
