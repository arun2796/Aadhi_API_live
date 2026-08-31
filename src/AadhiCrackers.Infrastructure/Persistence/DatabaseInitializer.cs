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
        "StockItems",
        "SystemSettings"
    };

    public static async Task InitializeAsync(
        AadhiDbContext context,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        await BaselineExistingSchemaAsync(context, logger, cancellationToken);
        await context.Database.MigrateAsync(cancellationToken);
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
            var historyTableExists = await TableExistsAsync(connection, "__EFMigrationsHistory", cancellationToken);
            var historyRowCount = historyTableExists
                ? await CountRowsAsync(connection, "__EFMigrationsHistory", cancellationToken)
                : 0;

            if (historyRowCount > 0)
            {
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

            var historyRepository = context.GetService<IHistoryRepository>();
            var createHistoryScript = historyRepository.GetCreateIfNotExistsScript();
            if (!string.IsNullOrWhiteSpace(createHistoryScript))
            {
                await context.Database.ExecuteSqlRawAsync(createHistoryScript, cancellationToken);
            }

            if (historyRowCount == 0)
            {
                var insertHistoryScript = historyRepository.GetInsertScript(new HistoryRow(firstMigrationId, "9.0.2"));
                await context.Database.ExecuteSqlRawAsync(insertHistoryScript, cancellationToken);

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

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
