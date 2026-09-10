using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging;

namespace AadhiCrackers.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    private static readonly string[] RequiredBaselineTables =
    {
        "AspNetRoles",
        "AspNetUsers",
        "Categories",
        "Customers",
        "Orders",
        "Products",
        "SystemSettings"
    };

    public static async Task InitializeAsync(
        AadhiDbContext context,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        // Everything below the guard (schema integrity patching, legacy baselining) is SQLite-only:
        // it uses sqlite_master, PRAGMA table_info and SQLite-dialect DDL, and stamps migration
        // history rows for the SQLite migration set. On any other provider (PostgreSQL/Npgsql,
        // whose migrations live in AadhiCrackers.Infrastructure.MigrationsPostgres) the schema is
        // managed exclusively by EF migrations, so we only run MigrateAsync.
        if (!context.Database.IsSqlite())
        {
            await context.Database.MigrateAsync(cancellationToken);
            return;
        }

        await EnsureSchemaIntegrityAsync(context, cancellationToken);
        await BaselineExistingSchemaAsync(context, logger, cancellationToken);
        await context.Database.MigrateAsync(cancellationToken);
    }

    private static async Task EnsureSchemaIntegrityAsync(AadhiDbContext context, CancellationToken cancellationToken)
    {
        var integrityConnection = context.Database.GetDbConnection();
        var shouldCloseIntegrityConnection = integrityConnection.State != ConnectionState.Open;
        if (shouldCloseIntegrityConnection)
        {
            await integrityConnection.OpenAsync(cancellationToken);
        }

        try
        {
            // Fresh database (no user tables yet): skip legacy schema patching entirely and
            // let EF migrations create the full schema from scratch.
            if (!await HasUserTablesAsync(integrityConnection, cancellationToken))
            {
                return;
            }
        }
        finally
        {
            if (shouldCloseIntegrityConnection)
            {
                await integrityConnection.CloseAsync();
            }
        }

        await context.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "ProductReviews" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_ProductReviews" PRIMARY KEY,
                "ProductId" TEXT NOT NULL,
                "CustomerId" TEXT NOT NULL,
                "CustomerName" TEXT NOT NULL,
                "Rating" INTEGER NOT NULL,
                "Title" TEXT NULL,
                "Comment" TEXT NOT NULL,
                "IsVerifiedPurchase" INTEGER NOT NULL,
                "IsApproved" INTEGER NOT NULL,
                "CreatedAtUtc" TEXT NOT NULL,
                "UpdatedAtUtc" TEXT NULL,
                "CreatedBy" TEXT NULL,
                "UpdatedBy" TEXT NULL,
                "IsDeleted" INTEGER NOT NULL,
                CONSTRAINT "FK_ProductReviews_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS "IX_ProductReviews_ProductId" ON "ProductReviews" ("ProductId");

            DROP TABLE IF EXISTS "ReturnOrderItems";
            DROP TABLE IF EXISTS "ReturnOrders";
            DROP TABLE IF EXISTS "SupplierBills";
            DROP TABLE IF EXISTS "GoodsReceiptItems";
            DROP TABLE IF EXISTS "GoodsReceipts";
            DROP TABLE IF EXISTS "PurchaseOrderItems";
            DROP TABLE IF EXISTS "PurchaseOrders";
            DROP TABLE IF EXISTS "StockMovements";
            DROP TABLE IF EXISTS "StockItems";
            DROP TABLE IF EXISTS "Refunds";
            DROP TABLE IF EXISTS "Warehouses";
            DROP TABLE IF EXISTS "Suppliers";

            CREATE TABLE IF NOT EXISTS "ProductCategories" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_ProductCategories" PRIMARY KEY,
                "ProductId" TEXT NOT NULL,
                "CategoryId" TEXT NOT NULL,
                "IsPrimary" INTEGER NOT NULL DEFAULT 0,
                "CreatedAtUtc" TEXT NOT NULL,
                "UpdatedAtUtc" TEXT NULL,
                "CreatedBy" TEXT NULL,
                "UpdatedBy" TEXT NULL,
                "IsDeleted" INTEGER NOT NULL DEFAULT 0,
                CONSTRAINT "FK_ProductCategories_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_ProductCategories_Categories_CategoryId" FOREIGN KEY ("CategoryId") REFERENCES "Categories" ("Id") ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS "IX_ProductCategories_ProductId" ON "ProductCategories" ("ProductId");
            CREATE INDEX IF NOT EXISTS "IX_ProductCategories_CategoryId" ON "ProductCategories" ("CategoryId");


            CREATE TABLE IF NOT EXISTS "LoginHistories" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_LoginHistories" PRIMARY KEY,
                "UserId" TEXT NULL,
                "Email" TEXT NOT NULL,
                "TimestampUtc" TEXT NOT NULL,
                "IpAddress" TEXT NULL,
                "UserAgent" TEXT NULL,
                "Success" INTEGER NOT NULL,
                "FailureReason" TEXT NULL,
                "CreatedAtUtc" TEXT NOT NULL,
                "UpdatedAtUtc" TEXT NULL,
                "CreatedBy" TEXT NULL,
                "UpdatedBy" TEXT NULL,
                "IsDeleted" INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS "IX_LoginHistories_Email" ON "LoginHistories" ("Email");
            CREATE INDEX IF NOT EXISTS "IX_LoginHistories_TimestampUtc" ON "LoginHistories" ("TimestampUtc");

            CREATE TABLE IF NOT EXISTS "AuditLogs" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_AuditLogs" PRIMARY KEY,
                "TimestampUtc" TEXT NOT NULL,
                "UserId" TEXT NULL,
                "UserName" TEXT NULL,
                "Role" TEXT NULL,
                "Action" INTEGER NOT NULL,
                "Module" TEXT NOT NULL,
                "EntityType" TEXT NOT NULL,
                "EntityId" TEXT NULL,
                "EntityName" TEXT NULL,
                "HttpMethod" TEXT NOT NULL,
                "RequestPath" TEXT NOT NULL,
                "CorrelationId" TEXT NOT NULL,
                "TraceId" TEXT NULL,
                "IpAddress" TEXT NULL,
                "UserAgent" TEXT NULL,
                "ClientApplication" TEXT NULL,
                "Severity" INTEGER NOT NULL,
                "Success" INTEGER NOT NULL,
                "FailureReason" TEXT NULL,
                "BeforeJson" TEXT NULL,
                "AfterJson" TEXT NULL,
                "ChangedFieldsJson" TEXT NULL,
                "MetadataJson" TEXT NULL,
                "CreatedAtUtc" TEXT NOT NULL,
                "UpdatedAtUtc" TEXT NULL,
                "CreatedBy" TEXT NULL,
                "UpdatedBy" TEXT NULL,
                "IsDeleted" INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS "IX_AuditLogs_TimestampUtc" ON "AuditLogs" ("TimestampUtc");
            CREATE INDEX IF NOT EXISTS "IX_AuditLogs_CorrelationId" ON "AuditLogs" ("CorrelationId");

            CREATE TABLE IF NOT EXISTS "RateLimitLogs" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_RateLimitLogs" PRIMARY KEY,
                "TimestampUtc" TEXT NOT NULL,
                "Endpoint" TEXT NOT NULL,
                "Policy" TEXT NOT NULL,
                "IpAddress" TEXT NULL,
                "RequestsCount" INTEGER NOT NULL,
                "BlockedCount" INTEGER NOT NULL,
                "Reason" TEXT NOT NULL,
                "CreatedAtUtc" TEXT NOT NULL,
                "UpdatedAtUtc" TEXT NULL,
                "CreatedBy" TEXT NULL,
                "UpdatedBy" TEXT NULL,
                "IsDeleted" INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS "SystemSettings" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_SystemSettings" PRIMARY KEY,
                "Key" TEXT NOT NULL,
                "Value" TEXT NOT NULL,
                "Group" TEXT NOT NULL,
                "Description" TEXT NULL,
                "IsEncrypted" INTEGER NOT NULL DEFAULT 0,
                "CreatedAtUtc" TEXT NOT NULL,
                "UpdatedAtUtc" TEXT NULL,
                "CreatedBy" TEXT NULL,
                "UpdatedBy" TEXT NULL,
                "IsDeleted" INTEGER NOT NULL DEFAULT 0
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_SystemSettings_Key" ON "SystemSettings" ("Key");
        """, cancellationToken);

        var connection = context.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            // Column migrations for Products
            await EnsureColumnAsync(connection, "Products", "RowVersion", "TEXT NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000'", cancellationToken);
            await EnsureColumnAsync(connection, "Products", "ProductType", "INTEGER NOT NULL DEFAULT 1", cancellationToken);
            await EnsureColumnAsync(connection, "Products", "SafetyInformation", "TEXT NULL", cancellationToken);
            await EnsureColumnAsync(connection, "Products", "ShortDescription", "TEXT NULL", cancellationToken);

            // Column migrations for OutboxMessages
            await EnsureColumnAsync(connection, "OutboxMessages", "NextAttemptAtUtc", "TEXT NULL", cancellationToken);
            await EnsureColumnAsync(connection, "OutboxMessages", "RetryCount", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
            await EnsureColumnAsync(connection, "OutboxMessages", "Error", "TEXT NULL", cancellationToken);
            await EnsureColumnAsync(connection, "OutboxMessages", "Status", "TEXT NOT NULL DEFAULT 'Pending'", cancellationToken);
            await EnsureColumnAsync(connection, "OutboxMessages", "ProcessedOnUtc", "TEXT NULL", cancellationToken);
            await EnsureColumnAsync(connection, "OutboxMessages", "OccurredOnUtc", "TEXT NOT NULL DEFAULT '2026-01-01T00:00:00Z'", cancellationToken);
            await EnsureColumnAsync(connection, "OutboxMessages", "PayloadJson", "TEXT NOT NULL DEFAULT '{}'", cancellationToken);

            // Column migrations for Payments
            await EnsureColumnAsync(connection, "Payments", "UtrNumber", "TEXT NULL", cancellationToken);
            await EnsureColumnAsync(connection, "Payments", "IdempotencyKey", "TEXT NULL", cancellationToken);

            // Column migrations for Orders
            await EnsureColumnAsync(connection, "Orders", "RowVersion", "TEXT NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000'", cancellationToken);
            await EnsureColumnAsync(connection, "Orders", "UtrNumber", "TEXT NULL", cancellationToken);
            await EnsureColumnAsync(connection, "Orders", "PaymentScreenshotUrl", "TEXT NULL", cancellationToken);
            await EnsureColumnAsync(connection, "Orders", "PaymentSubmittedAtUtc", "TEXT NULL", cancellationToken);
            await EnsureColumnAsync(connection, "Orders", "PaymentVerifiedAtUtc", "TEXT NULL", cancellationToken);
            await EnsureColumnAsync(connection, "Orders", "PaymentVerifiedBy", "TEXT NULL", cancellationToken);
            await EnsureColumnAsync(connection, "Orders", "PaymentVerificationNotes", "TEXT NULL", cancellationToken);

            // Column migrations for OrderItems
            await EnsureColumnAsync(connection, "OrderItems", "CostPriceAtSale", "TEXT NOT NULL DEFAULT '0'", cancellationToken);
            await EnsureColumnAsync(connection, "OrderItems", "VariantId", "TEXT NULL", cancellationToken);

            // Column migrations for ProductReviews
            await EnsureColumnAsync(connection, "ProductReviews", "OrderId", "TEXT NULL", cancellationToken);
            await EnsureColumnAsync(connection, "ProductReviews", "OrderItemId", "TEXT NULL", cancellationToken);
            await EnsureColumnAsync(connection, "ProductReviews", "Status", "TEXT NOT NULL DEFAULT 'Approved'", cancellationToken);

            // Column migrations for Promotions
            await EnsureColumnAsync(connection, "Promotions", "RowVersion", "TEXT NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000'", cancellationToken);

            // Update NULL RowVersion values (only for tables that exist)
            foreach (var rowVersionTable in new[] { "Products", "Orders", "Promotions" })
            {
                if (await TableExistsAsync(connection, rowVersionTable, cancellationToken))
                {
                    await context.Database.ExecuteSqlRawAsync(
                        $"""UPDATE "{rowVersionTable}" SET "RowVersion" = lower(hex(randomblob(16))) WHERE "RowVersion" IS NULL OR "RowVersion" = '' OR "RowVersion" = '00000000-0000-0000-0000-000000000000';""",
                        cancellationToken);
                }
            }
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task BaselineExistingSchemaAsync(
        AadhiDbContext context,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var firstMigrationId = context.Database.GetMigrations().FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstMigrationId))
        {
            return;
        }

        var connection = context.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;

        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            var historyRepository = context.GetService<IHistoryRepository>();
            var historyTableExists = await TableExistsAsync(connection, "__EFMigrationsHistory", cancellationToken);
            var historyRowCount = historyTableExists
                ? await CountRowsAsync(connection, "__EFMigrationsHistory", cancellationToken)
                : 0;

            if (historyRowCount > 0)
            {
                var bannersMigration = context.Database.GetMigrations().FirstOrDefault(m => m.Contains("AddHomepageBanners"));
                if (!string.IsNullOrWhiteSpace(bannersMigration) &&
                    await TableExistsAsync(connection, "ProductReviews", cancellationToken) &&
                    await ColumnExistsAsync(connection, "ProductReviews", "OrderId", cancellationToken) &&
                    !await MigrationHistoryRowExistsAsync(connection, bannersMigration, cancellationToken))
                {
                    var insertScript = historyRepository.GetInsertScript(new HistoryRow(bannersMigration, "9.0.2"));
                    try
                    {
                        await context.Database.ExecuteSqlRawAsync(insertScript, cancellationToken);
                    }
                    catch { }
                }
                return;
            }

            if (!await HasUserTablesAsync(connection, cancellationToken))
            {
                return;
            }

            foreach (var tableName in RequiredBaselineTables)
            {
                if (!await TableExistsAsync(connection, tableName, cancellationToken))
                {
                    throw new InvalidOperationException(
                        $"Existing database contains user tables but does not match the expected migration baseline. Missing table '{tableName}'.");
                }
            }

            var createHistoryScript = historyRepository.GetCreateIfNotExistsScript();
            if (!string.IsNullOrWhiteSpace(createHistoryScript))
            {
                await context.Database.ExecuteSqlRawAsync(createHistoryScript, cancellationToken);
            }

            // Ensure ProductReviews table exists before migration tries to ALTER it
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "ProductReviews" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_ProductReviews" PRIMARY KEY,
                    "ProductId" TEXT NOT NULL,
                    "CustomerId" TEXT NOT NULL,
                    "CustomerName" TEXT NOT NULL,
                    "Rating" INTEGER NOT NULL,
                    "Title" TEXT NULL,
                    "Comment" TEXT NOT NULL,
                    "IsVerifiedPurchase" INTEGER NOT NULL,
                    "IsApproved" INTEGER NOT NULL,
                    "CreatedAtUtc" TEXT NOT NULL,
                    "UpdatedAtUtc" TEXT NULL,
                    "CreatedBy" TEXT NULL,
                    "UpdatedBy" TEXT NULL,
                    "IsDeleted" INTEGER NOT NULL,
                    CONSTRAINT "FK_ProductReviews_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS "IX_ProductReviews_ProductId" ON "ProductReviews" ("ProductId");
            """, cancellationToken);

            if (historyRowCount == 0)
            {
                var migrations = context.Database.GetMigrations().ToList();
                foreach (var migrationId in migrations)
                {
                    if (migrationId == firstMigrationId)
                    {
                        var insertHistoryScript = historyRepository.GetInsertScript(new HistoryRow(migrationId, "9.0.2"));
                        await context.Database.ExecuteSqlRawAsync(insertHistoryScript, cancellationToken);
                    }
                    else if (migrationId.Contains("AddPromotionRedemptions") && await TableExistsAsync(connection, "PromotionRedemptions", cancellationToken))
                    {
                        var insertHistoryScript = historyRepository.GetInsertScript(new HistoryRow(migrationId, "9.0.2"));
                        await context.Database.ExecuteSqlRawAsync(insertHistoryScript, cancellationToken);
                    }
                    else if (migrationId.Contains("AddQuotesAndQuoteItems") && await TableExistsAsync(connection, "Quotes", cancellationToken))
                    {
                        var insertHistoryScript = historyRepository.GetInsertScript(new HistoryRow(migrationId, "9.0.2"));
                        await context.Database.ExecuteSqlRawAsync(insertHistoryScript, cancellationToken);
                    }
                    else if (migrationId.Contains("AddHomepageBanners") && await TableExistsAsync(connection, "HomepageBanners", cancellationToken))
                    {
                        var insertHistoryScript = historyRepository.GetInsertScript(new HistoryRow(migrationId, "9.0.2"));
                        await context.Database.ExecuteSqlRawAsync(insertHistoryScript, cancellationToken);
                    }
                    else if (migrationId.Contains("AddOtpWishlistRewardsAndDelivery") && await TableExistsAsync(connection, "OtpVerifications", cancellationToken))
                    {
                        var insertHistoryScript = historyRepository.GetInsertScript(new HistoryRow(migrationId, "9.0.2"));
                        await context.Database.ExecuteSqlRawAsync(insertHistoryScript, cancellationToken);
                    }
                    else if (migrationId.Contains("AddEnquiriesAndBannerPlacement") && (await TableExistsAsync(connection, "Enquiries", cancellationToken) || await ColumnExistsAsync(connection, "HomepageBanners", "Placement", cancellationToken)))
                    {
                        var insertHistoryScript = historyRepository.GetInsertScript(new HistoryRow(migrationId, "9.0.2"));
                        await context.Database.ExecuteSqlRawAsync(insertHistoryScript, cancellationToken);
                    }
                    else if (migrationId.Contains("AddProductComboItems") && await TableExistsAsync(connection, "ProductComboItems", cancellationToken))
                    {
                        // A database created by EnsureCreated from the current model already has the
                        // combo table, so the CreateTable in this migration would fail. Stamp it.
                        var insertHistoryScript = historyRepository.GetInsertScript(new HistoryRow(migrationId, "9.0.2"));
                        await context.Database.ExecuteSqlRawAsync(insertHistoryScript, cancellationToken);
                    }
                    else if (migrationId.Contains("AddOrderItemCompareAtPriceSnapshot") && await ColumnExistsAsync(connection, "OrderItems", "CompareAtPriceSnapshot", cancellationToken))
                    {
                        // A database created by EnsureCreated from the current model already has this
                        // column, and — unlike Orders, which DropRemainingObsoleteTables rebuilds —
                        // OrderItems is never rebuilt by a later migration, so the AddColumn in this
                        // migration would fail with "duplicate column name". Stamp it.
                        var insertHistoryScript = historyRepository.GetInsertScript(new HistoryRow(migrationId, "9.0.2"));
                        await context.Database.ExecuteSqlRawAsync(insertHistoryScript, cancellationToken);
                    }
                }

                logger.LogWarning(
                    "Existing database without migration history was baselined to migration {MigrationId}. Pending migrations will be applied next.",
                    firstMigrationId);
            }
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task<bool> HasUserTablesAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(1)
            FROM sqlite_master
            WHERE type = 'table'
              AND name <> '__EFMigrationsHistory'
              AND name NOT LIKE 'sqlite_%';
            """;

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static async Task<bool> TableExistsAsync(
        DbConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(1)
            FROM sqlite_master
            WHERE type = 'table'
              AND name = $tableName;
            """;

        AddParameter(command, "$tableName", tableName);

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static async Task<long> CountRowsAsync(
        DbConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(1) FROM \"{tableName}\";";
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<bool> MigrationHistoryRowExistsAsync(
        DbConnection connection,
        string migrationId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(1)
            FROM "__EFMigrationsHistory"
            WHERE "MigrationId" = $migrationId;
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$migrationId";
        parameter.Value = migrationId;
        command.Parameters.Add(parameter);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static async Task<bool> ColumnExistsAsync(
        DbConnection connection,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{tableName}\");";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static async Task EnsureColumnAsync(
        DbConnection connection,
        string tableName,
        string columnName,
        string columnDefinition,
        CancellationToken cancellationToken)
    {
        if (await TableExistsAsync(connection, tableName, cancellationToken))
        {
            if (!await ColumnExistsAsync(connection, tableName, columnName, cancellationToken))
            {
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = $"ALTER TABLE \"{tableName}\" ADD \"{columnName}\" {columnDefinition};";
                try
                {
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                }
                catch { }
            }
        }
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
