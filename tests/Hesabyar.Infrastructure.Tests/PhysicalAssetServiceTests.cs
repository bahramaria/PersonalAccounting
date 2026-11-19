using Hesabyar.Infrastructure.Persistence;
using Hesabyar.Infrastructure.Persistence.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Hesabyar.Infrastructure.Tests;

public sealed class PhysicalAssetServiceTests
{
    [Fact]
    public async Task Assets_AreSeparateFromLedgerAndKeepValuationHistory()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync();

        var service = new PhysicalAssetService(new SharedConnectionFactory(options));
        var assetId = await service.CreateAsync(new CreatePhysicalAssetRequest(
            "Gold", PhysicalAssetType.Gold, 12.5m, "gram", "IRR",
            new DateOnly(2026, 1, 1), 900_000_000, null));

        await service.AddValuationAsync(assetId, new DateOnly(2026, 11, 19), 1_050_000_000, "IRR");
        var asset = Assert.Single(await service.GetAsync());
        Assert.Equal(12.5m, asset.Quantity);
        Assert.Equal(1_050_000_000, asset.CurrentValueMinor);
        Assert.Equal(150_000_000, asset.UnrealizedGainMinor);
        Assert.Equal(0, await CountLedgerTransactionsAsync(options));
    }

    [Fact]
    public async Task Valuation_RejectsCurrencyMismatchAndNegativeValues()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync();

        var service = new PhysicalAssetService(new SharedConnectionFactory(options));
        var id = await service.CreateAsync(new CreatePhysicalAssetRequest(
            "Car", PhysicalAssetType.Vehicle, 1m, "unit", "IRR", null, 0, null));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddValuationAsync(id, new DateOnly(2026, 11, 19), 100, "USD"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.AddValuationAsync(id, new DateOnly(2026, 11, 19), -1, "IRR"));
    }

    private static async Task<int> CountLedgerTransactionsAsync(DbContextOptions<AppDbContext> options)
    {
        await using var db = new AppDbContext(options);
        return await db.Transactions.CountAsync();
    }

    private sealed class SharedConnectionFactory(DbContextOptions<AppDbContext> options)
        : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AppDbContext(options));
    }
}
