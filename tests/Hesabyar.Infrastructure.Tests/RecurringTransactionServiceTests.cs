using Hesabyar.Application.Common;
using Hesabyar.Domain.Accounts;
using Hesabyar.Infrastructure.Persistence;
using Hesabyar.Infrastructure.Persistence.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Hesabyar.Infrastructure.Tests;

public sealed class RecurringTransactionServiceTests
{
    [Fact]
    public async Task GenerateTransaction_IsAtomicAndIdempotent()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var setup = new AppDbContext(options);
        await setup.Database.MigrateAsync();

        var paymentAccount = new DbAccount
        {
            Name = "Cash",
            Type = AccountType.Asset,
            Kind = AccountKind.Cash,
            CurrencyCode = "IRR"
        };
        var expenseCategory = new DbAccount
        {
            Name = "Rent",
            Type = AccountType.Expense,
            Kind = AccountKind.Category,
            CurrencyCode = "IRR"
        };
        setup.Accounts.AddRange(paymentAccount, expenseCategory);
        await setup.SaveChangesAsync();

        var clock = new FixedClock();
        var factory = new TestDbContextFactory(options);
        var scheduler = new RecurringOccurrenceScheduler(factory, clock);
        var service = new RecurringTransactionService(factory, clock, scheduler);
        var scheduleId = await service.CreateAsync(new CreateRecurringScheduleRequest(
            "Monthly rent", "Expense", "IRR", 25_000_000,
            paymentAccount.Id, expenseCategory.Id,
            new DateOnly(2026, 11, 16), null, 1, "Month"));

        Assert.Equal(1, await service.PrepareDueOccurrencesAsync(new DateOnly(2026, 11, 16)));
        var occurrence = Assert.Single(await service.GetOccurrencesAsync(scheduleId));
        Assert.Equal("Pending", occurrence.Status);

        var transactionId = await service.GenerateTransactionAsync(occurrence.Id);
        var repeatedTransactionId = await service.GenerateTransactionAsync(occurrence.Id);

        Assert.NotNull(transactionId);
        Assert.Equal(transactionId, repeatedTransactionId);
        setup.ChangeTracker.Clear();

        var transactions = await setup.Transactions.Include(x => x.Postings).ToListAsync();
        var transaction = Assert.Single(transactions);
        Assert.Equal(transactionId, transaction.Id);
        Assert.Equal("Monthly rent", transaction.Description);
        Assert.True(transaction.IsConfirmed);
        Assert.Equal(2, transaction.Postings.Count);
        Assert.Equal(0, transaction.Postings.Sum(x => x.AmountMinor));

        var savedOccurrence = await setup.RecurringTransactionOccurrences.SingleAsync();
        Assert.Equal("Generated", savedOccurrence.Status);
        Assert.Equal(transaction.Id, savedOccurrence.TransactionId);
    }

    [Fact]
    public async Task AccountAndCategoryChoices_ExcludeArchivedAndWrongTypeAccounts()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var setup = new AppDbContext(options);
        await setup.Database.MigrateAsync();

        setup.Accounts.AddRange(
            new DbAccount
            {
                Name = "Cash",
                Type = AccountType.Asset,
                Kind = AccountKind.Cash,
                CurrencyCode = "IRR"
            },
            new DbAccount
            {
                Name = "Archived cash",
                Type = AccountType.Asset,
                Kind = AccountKind.Cash,
                CurrencyCode = "IRR",
                IsArchived = true
            },
            new DbAccount
            {
                Name = "Rent",
                Type = AccountType.Expense,
                Kind = AccountKind.Category,
                CurrencyCode = "IRR"
            },
            new DbAccount
            {
                Name = "Salary",
                Type = AccountType.Income,
                Kind = AccountKind.Category,
                CurrencyCode = "IRR"
            });
        await setup.SaveChangesAsync();

        var clock = new FixedClock();
        var factory = new TestDbContextFactory(options);
        var service = new RecurringTransactionService(factory, clock, new RecurringOccurrenceScheduler(factory, clock));

        var accounts = await service.GetAccountChoicesAsync();
        var categories = await service.GetCategoryChoicesAsync("Expense");

        var account = Assert.Single(accounts);
        Assert.Equal("Cash", account.Name);
        Assert.Equal("Asset", account.Type);
        var category = Assert.Single(categories);
        Assert.Equal("Rent", category.Name);
    }

    [Fact]
    public async Task SetPaused_DoesNotResumeAnExpiredSchedule()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var setup = new AppDbContext(options);
        await setup.Database.MigrateAsync();

        var account = new DbAccount
        {
            Name = "Cash",
            Type = AccountType.Asset,
            Kind = AccountKind.Cash,
            CurrencyCode = "IRR"
        };
        var category = new DbAccount
        {
            Name = "Rent",
            Type = AccountType.Expense,
            Kind = AccountKind.Category,
            CurrencyCode = "IRR"
        };
        setup.Accounts.AddRange(account, category);
        await setup.SaveChangesAsync();

        var clock = new FixedClock();
        var factory = new TestDbContextFactory(options);
        var service = new RecurringTransactionService(factory, clock, new RecurringOccurrenceScheduler(factory, clock));
        var id = await service.CreateAsync(new CreateRecurringScheduleRequest(
            "Expired rent", "Expense", "IRR", 1000, account.Id, category.Id,
            new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 15), 1, "Month"));

        Assert.True(await service.SetPausedAsync(id, true));
        Assert.False(await service.SetPausedAsync(id, false));
        Assert.True(Assert.Single(await service.GetSchedulesAsync()).IsPaused);
    }

    [Fact]
    public async Task GenerateTransaction_ReturnsNullWhenLinkedAccountWasArchived()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var setup = new AppDbContext(options);
        await setup.Database.MigrateAsync();

        var account = new DbAccount
        {
            Name = "Cash",
            Type = AccountType.Asset,
            Kind = AccountKind.Cash,
            CurrencyCode = "IRR"
        };
        var category = new DbAccount
        {
            Name = "Rent",
            Type = AccountType.Expense,
            Kind = AccountKind.Category,
            CurrencyCode = "IRR"
        };
        setup.Accounts.AddRange(account, category);
        await setup.SaveChangesAsync();

        var clock = new FixedClock();
        var factory = new TestDbContextFactory(options);
        var scheduler = new RecurringOccurrenceScheduler(factory, clock);
        var service = new RecurringTransactionService(factory, clock, scheduler);
        var scheduleId = await service.CreateAsync(new CreateRecurringScheduleRequest(
            "Monthly rent", "Expense", "IRR", 1000, account.Id, category.Id,
            new DateOnly(2026, 11, 16), null, 1, "Month"));
        Assert.Equal(1, await service.PrepareDueOccurrencesAsync(new DateOnly(2026, 11, 16)));
        var occurrence = Assert.Single(await service.GetOccurrencesAsync(scheduleId));

        account.IsArchived = true;
        await setup.SaveChangesAsync();

        Assert.Null(await service.GenerateTransactionAsync(occurrence.Id));
        Assert.Empty(await setup.Transactions.ToListAsync());
    }

    [Fact]
    public async Task Create_RejectsUnsupportedTransactionKind()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using (var setup = new AppDbContext(options))
            await setup.Database.MigrateAsync();

        var clock = new FixedClock();
        var factory = new TestDbContextFactory(options);
        var service = new RecurringTransactionService(
            factory, clock, new RecurringOccurrenceScheduler(factory, clock));

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(new CreateRecurringScheduleRequest(
            "Unsupported", "Transfer", "IRR", 1000, Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 11, 16), null, 1, "Month")));
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
