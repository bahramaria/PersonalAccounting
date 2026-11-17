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

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
