using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AadhiCrackers.Infrastructure.Persistence;

/// <summary>
/// Brings the PostgreSQL schema up to date at startup.
///
/// The schema is owned exclusively by the EF migrations in
/// <c>AadhiCrackers.Infrastructure.MigrationsPostgres</c>. This type deliberately contains no
/// schema patching, no table sniffing and no dialect-specific DDL: if a column is missing, the
/// answer is a migration, not a repair routine that drifts away from the migration history.
/// </summary>
public static class DatabaseInitializer
{
    public static async Task InitializeAsync(
        AadhiDbContext context,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();

        if (pending.Count == 0)
        {
            logger.LogInformation("Database schema is up to date; no pending migrations.");
            return;
        }

        logger.LogInformation(
            "Applying {Count} pending migration(s): {Migrations}",
            pending.Count,
            string.Join(", ", pending));

        await context.Database.MigrateAsync(cancellationToken);

        logger.LogInformation("Database schema migrated successfully.");
    }
}
