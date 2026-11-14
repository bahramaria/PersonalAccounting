using Hesabyar.Domain.Accounts;
using Hesabyar.Infrastructure.Persistence;
using Hesabyar.Infrastructure.Persistence.Entities;
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
            var safetyBackup = await service.RestoreBackupAsync(backupPath, "correct horse battery");
            Assert.True(File.Exists(safetyBackup));
            await using (var db = factory.CreateDbContext())
                Assert.Equal("Cash", await db.Accounts.Select(x => x.Name).SingleAsync());
        }
        finally
        {
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

    private sealed class TestDbContextFactory(string connectionString) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connectionString).Options;
            return new AppDbContext(options);
        }
        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CreateDbContext());
        }
    }
}