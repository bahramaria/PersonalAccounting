using Hesabyar.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Hesabyar.Infrastructure.Tests;

public sealed class ReminderServiceTests
{
    [Fact]
    public async Task CreateUpdateCompleteAndReopen_PreserveLifecycleRules()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using (var setup = new AppDbContext(options))
            await setup.Database.MigrateAsync();

        var now = new DateTimeOffset(2026, 11, 16, 12, 30, 0, TimeSpan.Zero);
        var service = new ReminderService(new TestDbContextFactory(options), new FixedTimeProvider(now));
        var id = await service.CreateAsync(new CreateReminderRequest(
            "  Pay electricity  ", " Bill ", new DateOnly(2026, 11, 20),
            new DateTimeOffset(2026, 11, 20, 8, 0, 0, TimeSpan.FromHours(3.5)), "  monthly bill  "));

        var created = Assert.Single(await service.GetAsync());
        Assert.Equal("Pay electricity", created.Title);
        Assert.Equal("Bill", created.Kind);
        Assert.Equal("monthly bill", created.Note);
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 4, 30, 0, TimeSpan.Zero), created.NotifyAtUtc);

        Assert.True(await service.UpdateAsync(id, new UpdateReminderRequest(
            "Pay gas", "Bill", new DateOnly(2026, 11, 22))));
        Assert.Equal("Pay gas", Assert.Single(await service.GetAsync()).Title);

        Assert.True(await service.CompleteAsync(id));
        Assert.True(await service.CompleteAsync(id));
        Assert.Empty(await service.GetAsync());
        var completed = Assert.Single(await service.GetAsync(includeCompleted: true));
        Assert.True(completed.IsCompleted);
        Assert.Equal(now, completed.CompletedAtUtc);
        Assert.Null(completed.NotifyAtUtc);

        Assert.False(await service.UpdateAsync(id, new UpdateReminderRequest(
            "Should not update", "Bill", new DateOnly(2026, 11, 23))));
        Assert.True(await service.ReopenAsync(id));
        Assert.False(Assert.Single(await service.GetAsync()).IsCompleted);
    }

    [Fact]
    public async Task Archive_RemovesReminderFromNormalQueries()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using (var setup = new AppDbContext(options))
            await setup.Database.MigrateAsync();

        var service = new ReminderService(new TestDbContextFactory(options), TimeProvider.System);
        var id = await service.CreateAsync(new CreateReminderRequest(
            "Archived", "General", new DateOnly(2026, 12, 1)));

        Assert.True(await service.ArchiveAsync(id));
        Assert.Empty(await service.GetAsync(includeCompleted: true));
        Assert.False(await service.ArchiveAsync(id));
    }

    [Fact]
    public async Task Create_RejectsInvalidTitle()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using (var setup = new AppDbContext(options))
            await setup.Database.MigrateAsync();

        var service = new ReminderService(new TestDbContextFactory(options), TimeProvider.System);
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(
            new CreateReminderRequest(" ", "General", new DateOnly(2026, 12, 1))));
    }

    private sealed class TestDbContextFactory(DbContextOptions<AppDbContext> options)
        : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
