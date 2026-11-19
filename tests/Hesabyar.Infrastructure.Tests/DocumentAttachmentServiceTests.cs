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
        var directory = Path.Combine(Path.GetTempPath(), "HesabyarAttachments", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        try
        {
            await using (var db = new AppDbContext(options))
            {
                await db.Database.MigrateAsync();
                db.PhysicalAssets.Add(new DbPhysicalAsset
                {
                    Name = "Gold", Type = Domain.Assets.PhysicalAssetType.Gold,
                    Quantity = 1m, CurrencyCode = "IRR", AcquisitionCostMinor = 100
                });
                await db.SaveChangesAsync();
            }
            var assetId = await GetAssetIdAsync(options);
            var service = new DocumentAttachmentService(new SharedFactory(options), directory, new TestKeyProvider());
            var pdf = "%PDF-1.7\nreceipt\n%%EOF"u8.ToArray();
            var id = await service.AddAsync(new MemoryStream(pdf), "../../receipt.pdf", physicalAssetId: assetId);

            var metadata = Assert.Single(await service.GetAsync(physicalAssetId: assetId));
            Assert.Equal("receipt.pdf", metadata.FileName);
            Assert.Equal("application/pdf", metadata.ContentType);
            Assert.Equal(pdf, await service.ReadAsync(id));
            var stored = Assert.Single(Directory.GetFiles(directory));
            Assert.False((await File.ReadAllBytesAsync(stored)).AsSpan().SequenceEqual(pdf));

            await service.DeleteAsync(id);
            Assert.Empty(await service.GetAsync(physicalAssetId: assetId));
            Assert.False(File.Exists(stored));
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
        var directory = Path.Combine(Path.GetTempPath(), "HesabyarAttachments", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        try
        {
            await using (var db = new AppDbContext(options)) await db.Database.MigrateAsync();
            var service = new DocumentAttachmentService(new SharedFactory(options), directory, new TestKeyProvider());
            await Assert.ThrowsAsync<ArgumentException>(() =>
                service.AddAsync(new MemoryStream("%PDF-"u8.ToArray()), "x.pdf"));
            await Assert.ThrowsAsync<ArgumentException>(() =>
                service.AddAsync(new MemoryStream("%PDF-"u8.ToArray()), "x.pdf", Guid.NewGuid(), Guid.NewGuid()));
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                service.AddAsync(new MemoryStream("not a document"u8.ToArray()), "x.pdf", transactionId: Guid.NewGuid()));
            Assert.Empty(Directory.GetFiles(directory));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static async Task<Guid> GetAssetIdAsync(DbContextOptions<AppDbContext> options)
    {
        await using var db = new AppDbContext(options);
        return await db.PhysicalAssets.Select(x => x.Id).SingleAsync();
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
}
