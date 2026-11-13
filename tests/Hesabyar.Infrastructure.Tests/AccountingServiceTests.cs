using Hesabyar.Application.Accounts;
using Hesabyar.Application.Common;
using Hesabyar.Application.Transactions;
using Hesabyar.Domain.Accounts;
using Hesabyar.Domain.Ledger;
using Hesabyar.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Hesabyar.Infrastructure.Tests;

public sealed class AccountingServiceTests
{
    private static readonly DateOnly BusinessDate = new(2026, 11, 13);
    private static readonly DateTimeOffset NowUtc = new(2026, 11, 13, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateAccount_WithOpeningBalance_PersistsBalancedOpeningTransaction()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDatabase(connection);
        await db.Database.MigrateAsync();

        var repository = new EfAccountingRepository(db);
        var service = new AccountService(repository, new FixedClock(NowUtc));

        var result = await service.CreateAsync(new CreateAccountRequest(
            "Cash",
            AccountType.Asset,
            AccountKind.Cash,
            OpeningBalanceMinor: 2_500_000,
            OpeningDate: BusinessDate));

        Assert.True(result.IsSuccess);
        Assert.Equal(2_500_000, await repository.GetBalanceMinorAsync(result.Value, default));
        Assert.Equal(2, await db.Postings.CountAsync());
        Assert.True((await db.Transactions.SingleAsync()).IsConfirmed);
    }

    [Fact]
    public async Task CreateExpense_UpdatesBalancesThroughPostings()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDatabase(connection);
        await db.Database.MigrateAsync();

        var repository = new EfAccountingRepository(db);
        var clock = new FixedClock(NowUtc);
        var accounts = new AccountService(repository, clock);
        var transactions = new TransactionService(repository, clock);

        var bank = await accounts.CreateAsync(new CreateAccountRequest(
            "Bank", AccountType.Asset, AccountKind.Bank, OpeningBalanceMinor: 1_000_000, OpeningDate: BusinessDate));
        var food = await accounts.CreateAsync(new CreateAccountRequest(
            "Food", AccountType.Expense, AccountKind.Category));

        Assert.True(bank.IsSuccess);
        Assert.True(food.IsSuccess);

        var expense = await transactions.CreateExpenseAsync(new ExpenseRequest(
            BusinessDate, 125_000, bank.Value, food.Value, "Lunch"));

        Assert.True(expense.IsSuccess);
        Assert.Equal(875_000, await repository.GetBalanceMinorAsync(bank.Value, default));
        Assert.Equal(125_000, await repository.GetBalanceMinorAsync(food.Value, default));
        Assert.True(await db.Transactions.SingleAsync(x => x.Kind == TransactionKind.Expense) is { IsConfirmed: true });
    }

    [Fact]
    public async Task CreateIncome_AndTransfer_UseExpectedPostingDirections()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDatabase(connection);
        await db.Database.MigrateAsync();

        var repository = new EfAccountingRepository(db);
        var clock = new FixedClock(NowUtc);
        var accounts = new AccountService(repository, clock);
        var transactions = new TransactionService(repository, clock);

        var bank = await accounts.CreateAsync(new CreateAccountRequest(
            "Bank", AccountType.Asset, AccountKind.Bank, OpeningBalanceMinor: 2_000_000, OpeningDate: BusinessDate));
        var wallet = await accounts.CreateAsync(new CreateAccountRequest(
            "Wallet", AccountType.Asset, AccountKind.Wallet));
        var salary = await accounts.CreateAsync(new CreateAccountRequest(
            "Salary", AccountType.Income, AccountKind.Category));

        Assert.True(bank.IsSuccess);
        Assert.True(wallet.IsSuccess);
        Assert.True(salary.IsSuccess);

        var income = await transactions.CreateIncomeAsync(new IncomeRequest(
            BusinessDate, 500_000, bank.Value, salary.Value));
        var transfer = await transactions.CreateTransferAsync(new TransferRequest(
            BusinessDate, 300_000, bank.Value, wallet.Value));

        Assert.True(income.IsSuccess);
        Assert.True(transfer.IsSuccess);
        Assert.Equal(2_200_000, await repository.GetBalanceMinorAsync(bank.Value, default));
        Assert.Equal(300_000, await repository.GetBalanceMinorAsync(wallet.Value, default));
        Assert.Equal(-500_000, await repository.GetBalanceMinorAsync(salary.Value, default));
    }

    [Fact]
    public async Task CreateTransfer_ToSameAccount_IsRejected()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDatabase(connection);
        await db.Database.MigrateAsync();

        var repository = new EfAccountingRepository(db);
        var accounts = new AccountService(repository, new FixedClock(NowUtc));
        var transactions = new TransactionService(repository, new FixedClock(NowUtc));
        var bank = await accounts.CreateAsync(new CreateAccountRequest("Bank", AccountType.Asset, AccountKind.Bank));

        var result = await transactions.CreateTransferAsync(new TransferRequest(
            BusinessDate, 100, bank.Value, bank.Value));

        Assert.True(result.IsFailure);
        Assert.Equal("transaction.transfer.same_account", result.Error.Code);
        Assert.Empty(await db.Transactions.ToListAsync());
    }

    [Fact]
    public async Task CreateCategory_WithParent_RejectsNestedThirdLevel()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDatabase(connection);
        await db.Database.MigrateAsync();

        var service = new AccountService(new EfAccountingRepository(db), new FixedClock(NowUtc));
        var root = await service.CreateAsync(new CreateAccountRequest("Food", AccountType.Expense, AccountKind.Category));
        var child = await service.CreateAsync(new CreateAccountRequest(
            "Restaurant", AccountType.Expense, AccountKind.Category, ParentId: root.Value));
        var invalid = await service.CreateAsync(new CreateAccountRequest(
            "Fast food", AccountType.Expense, AccountKind.Category, ParentId: child.Value));

        Assert.True(root.IsSuccess);
        Assert.True(child.IsSuccess);
        Assert.True(invalid.IsFailure);
        Assert.Equal("account.parent.invalid", invalid.Error.Code);
    }

    private static AppDbContext CreateDatabase(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        return new AppDbContext(options);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
