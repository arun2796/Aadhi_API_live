using AadhiCrackers.Domain.Entities;
using AadhiCrackers.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AadhiCrackers.Infrastructure.Tests;

public class DatabaseInitializerTests
{
    [Fact]
    public async Task InitializeAsync_AppliesMigrations_ToAFreshSqliteDatabase()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");

        try
        {
            await using var context = new AadhiDbContext(CreateSqliteOptions(databasePath));

            await DatabaseInitializer.InitializeAsync(context, NullLogger.Instance);

            var appliedMigrations = await context.Database.GetAppliedMigrationsAsync();
            var pendingMigrations = await context.Database.GetPendingMigrationsAsync();

            appliedMigrations.Should().ContainSingle(migration => migration.EndsWith("InitialCreate"));
            pendingMigrations.Should().BeEmpty();
        }
        finally
        {
            DeleteDatabaseFile(databasePath);
        }
    }

    [Fact]
    public async Task InitializeAsync_BaselinesExistingEnsureCreatedDatabase_WithoutLosingData()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");

        try
        {
            await using (var legacyContext = new AadhiDbContext(CreateSqliteOptions(databasePath)))
            {
                await legacyContext.Database.EnsureCreatedAsync();

                legacyContext.Categories.Add(new Category
                {
                    Name = "Legacy Category",
                    Slug = "legacy-category",
                    IsActive = true
                });

                await legacyContext.SaveChangesAsync();
            }

            await using var migratedContext = new AadhiDbContext(CreateSqliteOptions(databasePath));

            await DatabaseInitializer.InitializeAsync(migratedContext, NullLogger.Instance);

            var appliedMigrations = await migratedContext.Database.GetAppliedMigrationsAsync();
            var hasLegacyCategory = await migratedContext.Categories.AnyAsync(category => category.Slug == "legacy-category");

            appliedMigrations.Should().ContainSingle(migration => migration.EndsWith("InitialCreate"));
            hasLegacyCategory.Should().BeTrue();
        }
        finally
        {
            DeleteDatabaseFile(databasePath);
        }
    }

    private static DbContextOptions<AadhiDbContext> CreateSqliteOptions(string databasePath)
    {
        return new DbContextOptionsBuilder<AadhiDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
    }

    private static void DeleteDatabaseFile(string databasePath)
    {
        SqliteConnection.ClearAllPools();

        foreach (var path in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
        {
            if (!File.Exists(path))
            {
                continue;
            }

            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    File.Delete(path);
                    break;
                }
                catch (IOException) when (attempt < 4)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }
}
