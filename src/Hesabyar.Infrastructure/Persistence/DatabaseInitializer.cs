using Hesabyar.Application.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Hesabyar.Infrastructure.Persistence;

public sealed class DatabaseInitializer(
    IDbContextFactory<AppDbContext> contextFactory,
    IClock clock)
{
    public async Task InitializeAsync(string backupDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var existingDatabasePath = GetExistingDatabasePath(db.Database.GetConnectionString());
        var pendingMigrations = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();

        if (pendingMigrations.Length == 0)
            return;

        if (existingDatabasePath is not null)
            await BackupExistingDatabaseAsync(db.Database.GetConnectionString()!, backupDirectory, cancellationToken);

        await db.Database.MigrateAsync(cancellationToken);
    }

    private static string? GetExistingDatabasePath(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return null;

        var connection = new SqliteConnectionStringBuilder(connectionString);
        var dataSource = connection.DataSource;

        if (string.IsNullOrWhiteSpace(dataSource) ||
            string.Equals(dataSource, ":memory:", StringComparison.OrdinalIgnoreCase) ||
            connection.Mode == SqliteOpenMode.Memory)
        {
            return null;
        }

        var fullPath = Path.GetFullPath(dataSource);
        return File.Exists(fullPath) ? fullPath : null;
    }

    private async Task BackupExistingDatabaseAsync(
        string connectionString,
        string backupDirectory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(backupDirectory);
        var timestamp = clock.UtcNow.ToString(
            "yyyyMMddTHHmmssfffZ",
            System.Globalization.CultureInfo.InvariantCulture);
        var backupPath = Path.Combine(
            backupDirectory,
            $"pre-migration-{timestamp}-{Guid.CreateVersion7():N}.db");

        await using var source = new SqliteConnection(connectionString);
        await using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = backupPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString());

        await source.OpenAsync(cancellationToken);
        await destination.OpenAsync(cancellationToken);
        source.BackupDatabase(destination);
    }
}
