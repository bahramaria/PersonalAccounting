using Hesabyar.Domain.Ledger;
using Hesabyar.Infrastructure.Persistence;
using Hesabyar.Infrastructure.Persistence.Entities;
using Hesabyar.Infrastructure.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Hesabyar.Infrastructure.Tests;

public sealed class DocumentAttachmentServiceTests
{
    [Fact]
    public async Task Attachment_IsEncryptedOnDisk_AndCanBeReadBack()
    {
        var directory = CreateDirectory();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        try
        {
            await using (var db = new AppDbContext(options))
                await db.Database.MigrateAsync();

            var transactionId = await CreateTransactionAsync(options);
            var service = CreateService(options, directory);
            var pdf = "%PDF-1.7\nreceipt\n%%EOF"u8.ToArray();
            var id = await service.AddAsync(new MemoryStream(pdf), "../../receipt.pdf", transactionId: transactionId);

            var metadata = Assert.Single(await service.GetAsync(transactionId: transactionId));
            Assert.Equal("receipt.pdf", metadata.FileName);
            Assert.Equal("application/pdf", metadata.ContentType);
            Assert.Equal(pdf, await service.ReadAsync(id));
            var stored = Assert.Single(Directory.GetFiles(directory));
            Assert.False((await File.ReadAllBytesAsync(stored)).AsSpan().SequenceEqual(pdf));

            await service.DeleteAsync(id);
            Assert.Empty(await service.GetAsync(transactionId: transactionId));
            Assert.False(File.Exists(stored));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Attachment_ReadRejectsTamperedAndMissingFiles()
    {
        var directory = CreateDirectory();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        try
        {
            await using (var db = new AppDbContext(options))
                await db.Database.MigrateAsync();

            var transactionId = await CreateTransactionAsync(options);
            var service = CreateService(options, directory);
            var id = await service.AddAsync(
                new MemoryStream("%PDF-1.7\nreceipt\n%%EOF"u8.ToArray()),
                "receipt.pdf", transactionId: transactionId);
            var stored = Assert.Single(Directory.GetFiles(directory));
            var encrypted = await File.ReadAllBytesAsync(stored);
            encrypted[^1] ^= 0x01;
            await File.WriteAllBytesAsync(stored, encrypted);

            await Assert.ThrowsAsync<InvalidDataException>(() => service.ReadAsync(id));

            File.Delete(stored);
            await Assert.ThrowsAsync<FileNotFoundException>(() => service.ReadAsync(id));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Attachment_RejectsInvalidOwnerAndUnsupportedFile()
    {
        var directory = CreateDirectory();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        try
        {
            await using (var db = new AppDbContext(options))
                await db.Database.MigrateAsync();

            var transactionId = await CreateTransactionAsync(options);
            var service = CreateService(options, directory);
            await Assert.ThrowsAsync<ArgumentException>(() =>
                service.AddAsync(new MemoryStream("%PDF-"u8.ToArray()), "x.pdf"));
            await Assert.ThrowsAsync<ArgumentException>(() =>
                service.AddAsync(new MemoryStream("%PDF-"u8.ToArray()), "x.pdf", transactionId, Guid.NewGuid()));
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                service.AddAsync(new MemoryStream("not a document"u8.ToArray()), "x.pdf", transactionId: transactionId));
            Assert.Empty(Directory.GetFiles(directory));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task CleanupOrphans_RemovesOnlyUnreferencedFilesOlderThanOneHour()
    {
        var directory = CreateDirectory();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        try
        {
            await using (var db = new AppDbContext(options))
                await db.Database.MigrateAsync();

            var transactionId = await CreateTransactionAsync(options);
            var now = DateTimeOffset.UtcNow;
            var service = CreateService(options, directory, new FixedTimeProvider(now));
            var knownId = await service.AddAsync(
                new MemoryStream("%PDF-1.7\nknown\n%%EOF"u8.ToArray()),
                "known.pdf", transactionId: transactionId);
            var knownFile = Assert.Single(Directory.GetFiles(directory));

            var oldOrphan = Path.Combine(directory, "old-orphan.hatt");
            var recentOrphan = Path.Combine(directory, "recent-orphan.hatt");
            await File.WriteAllBytesAsync(oldOrphan, [1, 2, 3]);
            await File.WriteAllBytesAsync(recentOrphan, [4, 5, 6]);
            File.SetLastWriteTimeUtc(oldOrphan, now.AddHours(-2).UtcDateTime);
            File.SetLastWriteTimeUtc(recentOrphan, now.AddMinutes(-30).UtcDateTime);

            var removed = await service.CleanupOrphansAsync();

            Assert.Equal(1, removed);
            Assert.False(File.Exists(oldOrphan));
            Assert.True(File.Exists(recentOrphan));
            Assert.True(File.Exists(knownFile));
            Assert.Single(await service.GetAsync(transactionId: transactionId));
            Assert.NotEqual(Guid.Empty, knownId);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static string CreateDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "HesabyarAttachments", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static DocumentAttachmentService CreateService(
        DbContextOptions<AppDbContext> options,
        string directory,
        TimeProvider? timeProvider = null) =>
        new(new SharedFactory(options), directory, new TestKeyProvider(), timeProvider ?? TimeProvider.System);

    private static async Task<Guid> CreateTransactionAsync(DbContextOptions<AppDbContext> options)
    {
        await using var db = new AppDbContext(options);
        var transaction = new DbLedgerTransaction
        {
            BusinessDate = new DateOnly(2026, 11, 26),
            Kind = TransactionKind.Expense,
            Source = TransactionSource.Manual,
            IsConfirmed = true
        };
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();
        return transaction.Id;
    }

    private sealed class SharedFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AppDbContext(options));
    }

    private sealed class TestKeyProvider : IDatabaseKeyProvider
    {
        public string GetDatabasePassword() => "attachment-test-key";
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
