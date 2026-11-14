using Hesabyar.Domain.Accounts;
using Hesabyar.Infrastructure.Persistence;
using Hesabyar.Infrastructure.Persistence.Entities;
using Hesabyar.Infrastructure.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Hesabyar.Infrastructure.Tests;

public sealed class BackupServiceTests
{
    [Fact]
    public async Task EncryptedBackup_CanBeInspectedAndRestored_WithSafetyBackup()
    {
        var directory = Path.Combine(Path.GetTempPath(), "HesabyarBackupTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "hesabyar.db");
        var backupPath = Path.Combine(directory, "manual.hbak");
        var connectionString = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = databasePath, ForeignKeys = true, Pooling = false
        }.ToString();
        var factory = new TestDbContextFactory(connectionString);
        var service = new BackupService(factory, databasePath, TimeProvider.System);
        try
        {
            await using (var db = factory.CreateDbContext())
            {
                await db.Database.MigrateAsync();
                db.Accounts.Add(new DbAccount { Name = "Cash", Type = AccountType.Asset, Kind = AccountKind.Cash, CurrencyCode = "IRR" });
                await db.SaveChangesAsync();
            }
            await service.CreateBackupAsync(backupPath, "correct horse battery");
            var summary = await service.InspectBackupAsync(backupPath, "correct horse battery");
            Assert.True(summary.IsEncrypted);
            Assert.True(summary.DatabaseSizeBytes > 0);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                service.InspectBackupAsync(backupPath, "incorrect password"));
            await using (var db = factory.CreateDbContext())
            {
                db.Accounts.RemoveRange(db.Accounts);
                await db.SaveChangesAsync();
            }
            await File.WriteAllTextAsync(databasePath + "-wal", "stale WAL sidecar");
            await File.WriteAllTextAsync(databasePath + "-shm", "stale SHM sidecar");
            var safetyBackup = await service.RestoreBackupAsync(backupPath, "correct horse battery");
            Assert.True(File.Exists(safetyBackup));
            Assert.False(File.Exists(databasePath + "-wal"));
            Assert.False(File.Exists(databasePath + "-shm"));
            await using (var db = factory.CreateDbContext())
                Assert.Equal("Cash", await db.Accounts.Select(x => x.Name).SingleAsync());
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task EncryptedDatabase_BackupRestore_ReencryptsRestoredDatabase()
    {
        var directory = Path.Combine(Path.GetTempPath(), "HesabyarBackupTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "encrypted.db");
        var backupPath = Path.Combine(directory, "encrypted.hbak");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath, ForeignKeys = true, Pooling = false
        }.ToString();
        var keyProvider = new TestDatabaseKeyProvider("stable-device-database-key");
        var factory = new TestDbContextFactory(connectionString, keyProvider);
        var service = new BackupService(factory, databasePath, TimeProvider.System, keyProvider);

        try
        {
            await using (var db = factory.CreateDbContext())
            {
                await db.Database.MigrateAsync();
                db.Accounts.Add(new DbAccount
                {
                    Name = "Encrypted Cash", Type = AccountType.Asset,
                    Kind = AccountKind.Cash, CurrencyCode = "IRR"
                });
                await db.SaveChangesAsync();
            }

            await service.CreateBackupAsync(backupPath, "portable backup password");
            await using (var db = factory.CreateDbContext())
            {
                db.Accounts.RemoveRange(db.Accounts);
                await db.SaveChangesAsync();
            }

            await service.RestoreBackupAsync(backupPath, "portable backup password");

            var header = new byte[16];
            await using (var stream = File.OpenRead(databasePath))
                await stream.ReadExactlyAsync(header);
            Assert.False(header.AsSpan().SequenceEqual("SQLite format 3\\0"u8));

            await using var restored = factory.CreateDbContext();
            Assert.Equal("Encrypted Cash", await restored.Accounts.Select(x => x.Name).SingleAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AutomaticBackup_UsesDeviceKeyEncryption()
    {
        var directory = Path.Combine(Path.GetTempPath(), "HesabyarBackupTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "automatic.db");
        var backupDirectory = Path.Combine(directory, "automatic");
        const string password = "automatic-device-key";
        var keyProvider = new TestDatabaseKeyProvider(password);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath, Pooling = false
        }.ToString();
        var factory = new TestDbContextFactory(connectionString, keyProvider);
        var service = new BackupService(factory, databasePath, TimeProvider.System, keyProvider);
        try
        {
            await using (var db = factory.CreateDbContext())
                await db.Database.MigrateAsync();
            await service.CreateDailyAutomaticBackupAsync(backupDirectory);
            var backupPath = Assert.Single(Directory.GetFiles(backupDirectory, "automatic-*.hbak"));
            var summary = await service.InspectBackupAsync(backupPath, password);
            Assert.True(summary.IsEncrypted);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task UnencryptedBackup_IsClearlyIdentified()
    {
        var directory = Path.Combine(Path.GetTempPath(), "HesabyarBackupTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "hesabyar.db");
        var backupPath = Path.Combine(directory, "plain.hbak");
        var connectionString = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = databasePath, Pooling = false
        }.ToString();
        var factory = new TestDbContextFactory(connectionString);
        var service = new BackupService(factory, databasePath, TimeProvider.System);
        try
        {
            await using (var db = factory.CreateDbContext())
                await db.Database.MigrateAsync();
            await service.CreateBackupAsync(backupPath);
            var summary = await service.InspectBackupAsync(backupPath);
            Assert.False(summary.IsEncrypted);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class TestDbContextFactory(
        string connectionString, IDatabaseKeyProvider? keyProvider = null) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext()
        {
            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connectionString);
            if (keyProvider is not null)
                optionsBuilder.AddInterceptors(new SqliteConnectionPragmaInterceptor(keyProvider));
            var options = optionsBuilder.Options;
            return new AppDbContext(options);
        }
        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CreateDbContext());
        }
    }

    private sealed class TestDatabaseKeyProvider(string password) : IDatabaseKeyProvider
    {
        public string GetDatabasePassword() => password;
    }
}