using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Application.Services;
using AadhiCrackers.Domain.Entities;
using AadhiCrackers.Domain.Enums;
using AadhiCrackers.Infrastructure.Persistence;
using AadhiCrackers.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AadhiCrackers.Infrastructure.Tests;

public class TransactionPersistenceTests
{
    [Fact]
    public async Task Transaction_RollsBackBusinessWrite_WhenCommandFailsBeforeCommit()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");

        try
        {
            await using (var context = new AadhiDbContext(CreateSqliteOptions(databasePath)))
            {
                await DatabaseInitializer.InitializeAsync(context, NullLogger.Instance);
                await using var transaction = await context.BeginTransactionAsync();

                context.Categories.Add(new Category { Name = "Rollback", Slug = "rollback", IsActive = true });
                await context.SaveChangesAsync();
                await transaction.RollbackAsync();
            }

            await using var verificationContext = new AadhiDbContext(CreateSqliteOptions(databasePath));
            (await verificationContext.Categories.AnyAsync(category => category.Slug == "rollback")).Should().BeFalse();
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task AuditAndOutbox_ArePersistedTogether_WhenCallerCommits()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");

        try
        {
            await using var context = new AadhiDbContext(CreateSqliteOptions(databasePath));
            await DatabaseInitializer.InitializeAsync(context, NullLogger.Instance);

            var auditService = new AuditLogService(context, new TestCurrentUserService(), new OutboxService(context));
            await using var transaction = await context.BeginTransactionAsync();

            await auditService.LogAsync(AuditAction.Create, "Tests", nameof(Category), entityName: "Atomic audit");
            await context.SaveChangesAsync();
            await transaction.CommitAsync();

            (await context.AuditLogs.CountAsync()).Should().Be(1);
            (await context.OutboxMessages.CountAsync()).Should().Be(1);
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    private static DbContextOptions<AadhiDbContext> CreateSqliteOptions(string databasePath) =>
        new DbContextOptionsBuilder<AadhiDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;

    private static void DeleteDatabaseFiles(string databasePath)
    {
        SqliteConnection.ClearAllPools();

        foreach (var path in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private sealed class TestCurrentUserService : ICurrentUserService
    {
        public string? UserId => "test-user";
        public string? UserName => "Test User";
        public string? Email => "test@example.com";
        public string? Role => "Admin";
        public string? IpAddress => null;
        public string? UserAgent => null;
        public string CorrelationId => "transaction-test";
        public bool IsAuthenticated => true;
    }
}
