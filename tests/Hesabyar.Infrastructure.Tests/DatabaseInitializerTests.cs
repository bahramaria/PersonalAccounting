using Hesabyar.Application.Common;
using Hesabyar.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Hesabyar.Infrastructure.Tests;

public sealed class DatabaseInitializerTests
{
    [Fact]
    public async Task InitializeAsync_AppliesMigrations_AndBacksUpExistingDatabase()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"hesabyar-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "accounting.db");
        var backupDirectory = Path.Combine(directory, "backups");
        var connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();

        try
        {
            await using (var connection = new SqliteConnection(connectionString))
                await connection.OpenAsync();

            var factory = new TestDbContextFactory(connectionString);
            var initializer = new DatabaseInitializer(factory, new FixedClock(
                new DateTimeOffset(2026, 11, 13, 10, 0, 0, TimeSpan.Zero)));

            await initializer.InitializeAsync(backupDirectory);

            await using var db = await factory.CreateDbContextAsync();
            var applied = await db.Database.GetAppliedMigrationsAsync();
            Assert.Contains("202611130001_InitialAccountingSchema", applied);

            var backup = Assert.Single(Directory.GetFiles(backupDirectory, "*.db"));
            await using var backupConnection = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = backup, Mode = SqliteOpenMode.ReadOnly }.ToString());
            await backupConnection.OpenAsync();
            await using var command = backupConnection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Accounts'";
            var tableCount = Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(0, tableCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class TestDbContextFactory(string connectionString) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connectionString).Options;
            return new AppDbContext(options);
        }

        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
