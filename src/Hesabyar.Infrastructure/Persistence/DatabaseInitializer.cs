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
        var pendingMigrations = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
        if (pendingMigrations.Length == 0)
            return;

        await BackupExistingDatabaseAsync(db, backupDirectory, cancellationToken);
        await db.Database.MigrateAsync(cancellationToken);
    }

    private async Task BackupExistingDatabaseAsync(
        AppDbContext db,
        string backupDirectory,
        CancellationToken cancellationToken)
    {
        var connectionString = db.Database.GetConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        var sourceBuilder = new SqliteConnectionStringBuilder(connectionString);
        var sourcePath = sourceBuilder.DataSource;
        if (string.IsNullOrWhiteSpace(sourcePath) ||
            string.Equals(sourcePath, ":memory:", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(sourcePath))
        {
            return;
        }

        Directory.CreateDirectory(backupDirectory);
        var timestamp = clock.UtcNow.ToString("yyyyMMddTHHmmssfffZ", System.Globalization.CultureInfo.InvariantCulture);
        var backupPath = Path.Combine(backupDirectory, $"pre-migration-{timestamp}.db");

        await using var source = new SqliteConnection(connectionString);
        await using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = backupPath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString());

        await source.OpenAsync(cancellationToken);
        await destination.OpenAsync(cancellationToken);
        source.BackupDatabase(destination);
    }
}
