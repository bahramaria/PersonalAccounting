using Hesabyar.Application.Accounts;
using Hesabyar.Application.Common;
using Hesabyar.Application.Planning;
using Hesabyar.Application.Transactions;
using Hesabyar.Domain.Accounts;
using Hesabyar.Domain.Ledger;
using Hesabyar.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Hesabyar.Infrastructure.Tests;

public sealed class PlanningFeatureTests
{
    private static readonly DateOnly BusinessDate = new(2026, 11, 17);
    private static readonly DateTimeOffset NowUtc = new(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Budget_CanBeCreatedEditedDeactivatedAndReactivated()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync();

        var repository = new EfAccountingRepository(db);
        var clock = new FixedClock(NowUtc);
        var accounts = new AccountService(repository, clock);
        var planningRepository = new PlanningRepository(db);
        var budgets = new BudgetService(planningRepository, clock);
        var bank = await accounts.CreateAsync(new CreateAccountRequest("Bank", AccountType.Asset, AccountKind.Bank,
            OpeningBalanceMinor: 500_000, OpeningDate: BusinessDate));
        var food = await accounts.CreateAsync(new CreateAccountRequest("Food", AccountType.Expense, AccountKind.Category));
        Assert.True(bank.IsSuccess);
        Assert.True(food.IsSuccess);

        var expense = await new TransactionService(repository, clock).CreateExpenseAsync(
            new ExpenseRequest(BusinessDate, 75_000, bank.Value, food.Value));
        Assert.True(expense.IsSuccess);

        var created = await budgets.CreateAsync(new CreateBudgetRequest(
            food.Value, BusinessDate.AddDays(-5), BusinessDate.AddDays(5), 100_000, "IRR"));
        Assert.True(created.IsSuccess);
        var first = Assert.Single(await budgets.GetAsync(BusinessDate, BusinessDate));
        Assert.Equal(75_000, first.SpentMinor);
        Assert.Equal(100_000, first.LimitMinor);

        Assert.True((await budgets.UpdateLimitAsync(created.Value, 150_000)).IsSuccess);
        Assert.True((await budgets.SetActiveAsync(created.Value, false)).IsSuccess);
        var inactive = Assert.Single(await budgets.GetAsync(BusinessDate, BusinessDate));
        Assert.False(inactive.IsActive);
        Assert.Equal(150_000, inactive.LimitMinor);

        Assert.True((await budgets.SetActiveAsync(created.Value, true)).IsSuccess);
        Assert.True(Assert.Single(await budgets.GetAsync(BusinessDate, BusinessDate)).IsActive);
    }

    [Fact]
    public async Task Tags_CanBeAssignedToTransactionsAndRenamedWithoutLosingLinks()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync();

        var repository = new EfAccountingRepository(db);
        var clock = new FixedClock(NowUtc);
        var accounts = new AccountService(repository, clock);
        var bank = await accounts.CreateAsync(new CreateAccountRequest("Bank", AccountType.Asset, AccountKind.Bank,
            OpeningBalanceMinor: 100_000, OpeningDate: BusinessDate));
        var food = await accounts.CreateAsync(new CreateAccountRequest("Food", AccountType.Expense, AccountKind.Category));
        Assert.True(bank.IsSuccess);
        Assert.True(food.IsSuccess);
        var transaction = await new TransactionService(repository, clock).CreateExpenseAsync(
            new ExpenseRequest(BusinessDate, 10_000, bank.Value, food.Value));
        Assert.True(transaction.IsSuccess);

        var tags = new TagService(new PlanningRepository(db), clock);
        var created = await tags.SaveAsync(null, "Travel", "#336699");
        Assert.True(created.IsSuccess);
        Assert.True((await tags.SetTransactionTagsAsync(transaction.Value, [created.Value])).IsSuccess);
        Assert.Contains(created.Value, await tags.GetForTransactionAsync(transaction.Value));

        var renamed = await tags.SaveAsync(created.Value, "Business travel", "#112233");
        Assert.True(renamed.IsSuccess);
        var tag = Assert.Single(await tags.GetAsync());
        Assert.Equal("Business travel", tag.Name);
        Assert.Equal("#112233", tag.Color);
        Assert.Contains(created.Value, await tags.GetForTransactionAsync(transaction.Value));
    }

    [Fact]
    public async Task FiscalPeriods_NumberDocumentsIndependentlyAndRejectOverlaps()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync();

        var repository = new EfAccountingRepository(db);
        var clock = new FixedClock(NowUtc);
        var accounts = new AccountService(repository, clock);
        var bank = await accounts.CreateAsync(new CreateAccountRequest("Bank", AccountType.Asset, AccountKind.Bank,
            OpeningBalanceMinor: 100_000, OpeningDate: BusinessDate));
        var food = await accounts.CreateAsync(new CreateAccountRequest("Food", AccountType.Expense, AccountKind.Category));
        Assert.True(bank.IsSuccess);
        Assert.True(food.IsSuccess);
        var transactions = new TransactionService(repository, clock);

        var first = await transactions.CreateExpenseAsync(new ExpenseRequest(BusinessDate, 1_000, bank.Value, food.Value));
        var second = await transactions.CreateExpenseAsync(new ExpenseRequest(BusinessDate, 2_000, bank.Value, food.Value));
        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        var firstTwo = (await transactions.GetTransactionsAsync()).OrderBy(x => x.DocumentNumber).ToArray();
        Assert.Equal(2, firstTwo.Length);
        Assert.All(firstTwo, x => Assert.StartsWith("1405-", x.DocumentNumber));
        Assert.NotEqual(firstTwo[0].DocumentNumber, firstTwo[1].DocumentNumber);

        var fiscal = new FiscalPeriodService(new PlanningRepository(db), clock);
        var next = await fiscal.CreateAsync(new CreateFiscalPeriodRequest("1406",
            new DateOnly(2027, 3, 21), new DateOnly(2028, 3, 19)));
        Assert.True(next.IsSuccess);
        var overlap = await fiscal.CreateAsync(new CreateFiscalPeriodRequest("Overlapping",
            new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31)));
        Assert.True(overlap.IsFailure);

        var nextDate = new DateOnly(2027, 4, 1);
        var nextExpense = await transactions.CreateExpenseAsync(
            new ExpenseRequest(nextDate, 3_000, bank.Value, food.Value));
        Assert.True(nextExpense.IsSuccess);
        var nextSnapshot = (await transactions.GetTransactionsAsync(fromDate: nextDate, toDate: nextDate)).Single();
        Assert.StartsWith("1406-000001", nextSnapshot.DocumentNumber);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
