using Hesabyar.Application.Common;
using Hesabyar.Domain.Accounts;
using Hesabyar.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Hesabyar.Infrastructure.Tests;

public sealed class DatabaseInitializerTests
{
    private static readonly DateTimeOffset FixedUtcNow =
        new(2026, 11, 13, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task InitializeAsync_BacksUpExistingDatabaseBeforeMigration_AndIsIdempotent()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "HesabyarTests",
            Guid.CreateVersion7().ToString("N"));
        Directory.CreateDirectory(directory);

        var databasePath = Path.Combine(directory, "accounting.db");
        var backupDirectory = Path.Combine(directory, "backups");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true,
            Pooling = false
        }.ToString();

        try
        {
            await using (var legacyConnection = new SqliteConnection(connectionString))
            {
                await legacyConnection.OpenAsync();
                await using var command = legacyConnection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE LegacyData (Id INTEGER NOT NULL);
                    INSERT INTO LegacyData (Id) VALUES (42);
                    """;
                await command.ExecuteNonQueryAsync();
            }

            var factory = new TestDbContextFactory(connectionString);
            var initializer = new DatabaseInitializer(factory, new FixedClock(FixedUtcNow));

            await initializer.InitializeAsync(backupDirectory);

            var backupPath = Assert.Single(Directory.GetFiles(backupDirectory, "pre-migration-*.db"));
            await using (var backupConnection = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = backupPath,
                    Mode = SqliteOpenMode.ReadOnly,
                    Pooling = false
                }.ToString()))
            {
                await backupConnection.OpenAsync();
                await using var command = backupConnection.CreateCommand();
                command.CommandText = "SELECT Id FROM LegacyData;";
                Assert.Equal(42L, (long)(await command.ExecuteScalarAsync())!);
            }

            await using (var db = factory.CreateDbContext())
            {
                var appliedMigrations = await db.Database.GetAppliedMigrationsAsync();
                Assert.Contains("202611130001_InitialAccountingSchema", appliedMigrations);

                await db.Database.OpenConnectionAsync();
                await using var foreignKeysCommand = db.Database.GetDbConnection().CreateCommand();
                foreignKeysCommand.CommandText = "PRAGMA foreign_keys;";
                Assert.Equal(1L, (long)(await foreignKeysCommand.ExecuteScalarAsync())!);

                await using var journalModeCommand = db.Database.GetDbConnection().CreateCommand();
                journalModeCommand.CommandText = "PRAGMA journal_mode;";
                Assert.Equal("wal", (string)(await journalModeCommand.ExecuteScalarAsync())!);
            }

            await initializer.InitializeAsync(backupDirectory);

            Assert.Single(Directory.GetFiles(backupDirectory, "pre-migration-*.db"));
            await AssertDefaultCategoriesAsync(factory);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task InitializeAsync_SeedsDefaultCategoriesOnce_AndPreservesUserChanges()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "HesabyarTests",
            Guid.CreateVersion7().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(directory, "accounting.db"),
                ForeignKeys = true,
                Pooling = false
            }.ToString();
            var factory = new TestDbContextFactory(connectionString);
            var initializer = new DatabaseInitializer(factory, new FixedClock(FixedUtcNow));

            await initializer.InitializeAsync(Path.Combine(directory, "backups"));

            Guid foodId;
            await using (var db = factory.CreateDbContext())
            {
                var categories = await db.Accounts
                    .Where(x => x.Kind == AccountKind.Category)
                    .ToListAsync();
                Assert.Equal(13, categories.Count);
                Assert.Equal(11, categories.Count(x => x.Type == AccountType.Expense));
                Assert.Equal(2, categories.Count(x => x.Type == AccountType.Income));

                var food = Assert.Single(categories.Where(x => x.SystemKey == "seed:expense:food"));
                foodId = food.Id;
                food.Name = "خوراک و رستوران";
                food.IsArchived = true;
                await db.SaveChangesAsync();
            }

            await initializer.InitializeAsync(Path.Combine(directory, "backups"));

            await using (var db = factory.CreateDbContext())
            {
                var categories = await db.Accounts
                    .Where(x => x.Kind == AccountKind.Category)
                    .ToListAsync();
                Assert.Equal(13, categories.Count);

                var food = Assert.Single(categories.Where(x => x.Id == foodId));
                Assert.Equal("خوراک و رستوران", food.Name);
                Assert.True(food.IsArchived);
            }
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task AssertDefaultCategoriesAsync(TestDbContextFactory factory)
    {
        await using var db = factory.CreateDbContext();
        var categories = await db.Accounts
            .Where(x => x.Kind == AccountKind.Category)
            .ToListAsync();
        Assert.Equal(13, categories.Count);
    }

    private sealed class TestDbContextFactory(string connectionString) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connectionString)
                .AddInterceptors(new SqliteConnectionPragmaInterceptor())
                .Options;

            return new AppDbContext(options);
        }

        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CreateDbContext());
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
