using Hesabyar.Application.Common;
using Hesabyar.Infrastructure.Persistence;
using Hesabyar.Infrastructure.Persistence.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Hesabyar.Infrastructure.Tests;

public sealed class RecurringOccurrenceSchedulerTests
{
    [Fact]
    public async Task PreparesMissedOccurrencesOnceAndPausesAtEndDate()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var setupDb = new AppDbContext(options);
        await setupDb.Database.MigrateAsync();

        var account = new DbAccount
        {
            Name = "Cash",
            Type = Hesabyar.Domain.Accounts.AccountType.Asset,
            Kind = Hesabyar.Domain.Accounts.AccountKind.Cash,
            CurrencyCode = "IRR"
        };
        var schedule = new DbRecurringTransaction
        {
            Title = "Rent",
            Kind = "Expense",
            CurrencyCode = "IRR",
            AmountMinor = 1000,
            AccountId = account.Id,
            StartDate = new DateOnly(2026, 1, 31),
            NextOccurrenceDate = new DateOnly(2026, 1, 31),
            EndDate = new DateOnly(2026, 3, 31),
            IntervalCount = 1,
            IntervalUnit = "Month"
        };
        setupDb.Accounts.Add(account);
        setupDb.RecurringTransactions.Add(schedule);
        await setupDb.SaveChangesAsync();

        var factory = new TestDbContextFactory(options);
        var scheduler = new RecurringOccurrenceScheduler(factory, new FixedClock());

        var prepared = await scheduler.PrepareDueOccurrencesAsync(new DateOnly(2026, 3, 31));
        var repeated = await scheduler.PrepareDueOccurrencesAsync(new DateOnly(2026, 3, 31));

        Assert.Equal(3, prepared);
        Assert.Equal(0, repeated);

        setupDb.ChangeTracker.Clear();
        var occurrences = await setupDb.RecurringTransactionOccurrences
            .OrderBy(x => x.OccurrenceDate)
            .Select(x => x.OccurrenceDate)
            .ToListAsync();
        Assert.Equal(
            new[] { new DateOnly(2026, 1, 31), new DateOnly(2026, 2, 28), new DateOnly(2026, 3, 31) },
            occurrences);

        var updatedSchedule = await setupDb.RecurringTransactions.SingleAsync();
        Assert.True(updatedSchedule.IsPaused);
        Assert.Equal(new DateOnly(2026, 4, 30), updatedSchedule.NextOccurrenceDate);
    }

    private sealed class TestDbContextFactory(DbContextOptions<AppDbContext> options)
        : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
    }
}