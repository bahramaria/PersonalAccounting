using Hesabyar.Application.Accounts;
using Hesabyar.Application.Common;
using Hesabyar.Application.Transactions;
using Hesabyar.Domain.Accounts;
using Hesabyar.Domain.Ledger;
using Hesabyar.Infrastructure.Persistence;
using Hesabyar.Infrastructure.Persistence.Entities;
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

    [Fact]
    public async Task RenameAccount_UpdatesNameWithoutChangingBalance()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDatabase(connection);
        await db.Database.MigrateAsync();

        var repository = new EfAccountingRepository(db);
        var service = new AccountService(repository, new FixedClock(NowUtc));
        var created = await service.CreateAsync(new CreateAccountRequest(
            "Old name", AccountType.Asset, AccountKind.Bank,
            OpeningBalanceMinor: 750_000, OpeningDate: BusinessDate));

        var renamed = await service.RenameAsync(created.Value, "New name");
        var account = await repository.GetAccountAsync(created.Value, default);

        Assert.True(created.IsSuccess);
        Assert.True(renamed.IsSuccess);
        Assert.Equal("New name", account!.Name);
        Assert.Equal(750_000, await repository.GetBalanceMinorAsync(created.Value, default));
    }

    [Fact]
    public async Task ArchiveAccount_HidesAccountButPreservesItsLedgerHistory()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDatabase(connection);
        await db.Database.MigrateAsync();

        var repository = new EfAccountingRepository(db);
        var service = new AccountService(repository, new FixedClock(NowUtc));
        var created = await service.CreateAsync(new CreateAccountRequest(
            "Bank", AccountType.Asset, AccountKind.Bank,
            OpeningBalanceMinor: 900_000, OpeningDate: BusinessDate));

        var archived = await service.ArchiveAsync(created.Value);
        var activeAccounts = await repository.GetAccountsAsync(includeArchived: false, default);
        var allAccounts = await repository.GetAccountsAsync(includeArchived: true, default);
        var balance = await repository.GetBalanceMinorAsync(created.Value, default);

        Assert.True(created.IsSuccess);
        Assert.True(archived.IsSuccess);
        Assert.DoesNotContain(activeAccounts, x => x.Id == created.Value);
        Assert.Contains(allAccounts, x => x.Id == created.Value && x.IsArchived);
        Assert.Equal(900_000, balance);
        Assert.Equal(2, await db.Postings.CountAsync());
    }

    [Fact]
    public async Task RenameArchivedAccount_IsRejected()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDatabase(connection);
        await db.Database.MigrateAsync();

        var service = new AccountService(new EfAccountingRepository(db), new FixedClock(NowUtc));
        var created = await service.CreateAsync(new CreateAccountRequest("Bank", AccountType.Asset, AccountKind.Bank));
        Assert.True((await service.ArchiveAsync(created.Value)).IsSuccess);

        var renamed = await service.RenameAsync(created.Value, "Renamed");

        Assert.True(renamed.IsFailure);
        Assert.Equal("account.archived", renamed.Error.Code);
    }

    [Fact]
    public async Task Category_CanBeCreatedRenamedAndArchivedWithoutChangingLedgerData()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDatabase(connection);
        await db.Database.MigrateAsync();

        var repository = new EfAccountingRepository(db);
        var service = new AccountService(repository, new FixedClock(NowUtc));

        var created = await service.CreateAsync(new CreateAccountRequest(
            "Food", AccountType.Expense, AccountKind.Category));
        Assert.True(created.IsSuccess);

        var renamed = await service.RenameAsync(created.Value, "Groceries");
        Assert.True(renamed.IsSuccess);

        var category = await repository.GetAccountAsync(created.Value, default);
        Assert.Equal("Groceries", category!.Name);
        Assert.Equal(AccountKind.Category, category.Kind);

        var archived = await service.ArchiveAsync(created.Value);
        Assert.True(archived.IsSuccess);
        Assert.True((await repository.GetAccountAsync(created.Value, default))!.IsArchived);
        Assert.Empty(await db.Postings.ToListAsync());
    }


    [Fact]
    public async Task Transactions_CanBeListedDuplicatedAndSoftDeleted()
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
        var food = await accounts.CreateAsync(new CreateAccountRequest("Food", AccountType.Expense, AccountKind.Category));
        var expense = await transactions.CreateExpenseAsync(new ExpenseRequest(BusinessDate, 125_000, bank.Value, food.Value, "Lunch"));

        Assert.True(expense.IsSuccess);
        var listed = await transactions.GetTransactionsAsync("Lunch");
        Assert.Single(listed);
        Assert.Equal(expense.Value, listed[0].Id);
        Assert.Equal(bank.Value, listed[0].AccountId);
        Assert.Equal(food.Value, listed[0].CounterpartyAccountId);

        var duplicate = await transactions.DuplicateAsync(expense.Value);
        Assert.True(duplicate.IsSuccess);
        Assert.Equal(2, (await transactions.GetTransactionsAsync()).Count);

        var deleted = await transactions.DeleteAsync(expense.Value);
        Assert.True(deleted.IsSuccess);
        var remaining = await transactions.GetTransactionsAsync();
        Assert.Single(remaining);
        Assert.Equal(875_000, await repository.GetBalanceMinorAsync(bank.Value, default));
        Assert.True(await db.Transactions.IgnoreQueryFilters().AnyAsync(x => x.Id == expense.Value && x.IsDeleted));
    }

    [Fact]
    public async Task UpdateTransaction_ReplacesActiveLedgerEntryAndPreservesOldRecord()
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
        var food = await accounts.CreateAsync(new CreateAccountRequest("Food", AccountType.Expense, AccountKind.Category));
        var travel = await accounts.CreateAsync(new CreateAccountRequest("Travel", AccountType.Expense, AccountKind.Category));
        var original = await transactions.CreateExpenseAsync(new ExpenseRequest(BusinessDate, 100_000, bank.Value, food.Value, "Old"));

        var updated = await transactions.UpdateAsync(original.Value,
            expense: new ExpenseRequest(BusinessDate, 200_000, bank.Value, travel.Value, "Updated"));

        Assert.True(updated.IsSuccess);
        var active = await transactions.GetTransactionsAsync();
        Assert.Single(active);
        Assert.Equal(updated.Value, active[0].Id);
        Assert.Equal("Updated", active[0].Description);
        Assert.Equal(800_000, await repository.GetBalanceMinorAsync(bank.Value, default));
        Assert.Equal(0, await repository.GetBalanceMinorAsync(food.Value, default));
        Assert.Equal(200_000, await repository.GetBalanceMinorAsync(travel.Value, default));
        Assert.True(await db.Transactions.IgnoreQueryFilters().AnyAsync(x => x.Id == original.Value && x.IsDeleted));
    }

    [Fact]
    public async Task UpdateTransaction_WithInvalidReplacement_PreservesOriginalLedgerEntry()
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
            "Bank", AccountType.Asset, AccountKind.Bank, OpeningBalanceMinor: 500_000, OpeningDate: BusinessDate));
        var food = await accounts.CreateAsync(new CreateAccountRequest("Food", AccountType.Expense, AccountKind.Category));
        var original = await transactions.CreateExpenseAsync(
            new ExpenseRequest(BusinessDate, 75_000, bank.Value, food.Value, "Original"));

        var invalidUpdate = await transactions.UpdateAsync(original.Value,
            income: new IncomeRequest(BusinessDate, 90_000, bank.Value, food.Value, "Invalid"));

        Assert.True(invalidUpdate.IsFailure);
        Assert.Equal("transaction.update.invalid", invalidUpdate.Error.Code);
        var active = await transactions.GetTransactionsAsync();
        Assert.Single(active);
        Assert.Equal(original.Value, active[0].Id);
        Assert.Equal(425_000, await repository.GetBalanceMinorAsync(bank.Value, default));
        Assert.False(await db.Transactions.IgnoreQueryFilters().AnyAsync(x => x.Id == original.Value && x.IsDeleted));
    }

    [Fact]
    public async Task Transactions_CanBeFilteredByDateAccountCategoryAndAmount()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDatabase(connection);
        await db.Database.MigrateAsync();

        var repository = new EfAccountingRepository(db);
        var clock = new FixedClock(NowUtc);
        var accounts = new AccountService(repository, clock);
        var transactions = new TransactionService(repository, clock);

        var bank = await accounts.CreateAsync(new CreateAccountRequest("Bank", AccountType.Asset, AccountKind.Bank,
            OpeningBalanceMinor: 1_000_000, OpeningDate: BusinessDate));
        var wallet = await accounts.CreateAsync(new CreateAccountRequest("Wallet", AccountType.Asset, AccountKind.Wallet));
        var food = await accounts.CreateAsync(new CreateAccountRequest("Food", AccountType.Expense, AccountKind.Category));
        var travel = await accounts.CreateAsync(new CreateAccountRequest("Travel", AccountType.Expense, AccountKind.Category));

        var recentFood = await transactions.CreateExpenseAsync(
            new ExpenseRequest(BusinessDate, 100_000, bank.Value, food.Value, "Groceries"));
        var olderTravel = await transactions.CreateExpenseAsync(
            new ExpenseRequest(BusinessDate.AddDays(-2), 300_000, bank.Value, travel.Value, "Trip"));
        var walletFood = await transactions.CreateExpenseAsync(
            new ExpenseRequest(BusinessDate, 50_000, wallet.Value, food.Value, "Snack"));

        Assert.True(recentFood.IsSuccess);
        Assert.True(olderTravel.IsSuccess);
        Assert.True(walletFood.IsSuccess);

        Assert.Equal(2, (await transactions.GetTransactionsAsync(fromDate: BusinessDate)).Count);
        Assert.Equal(2, (await transactions.GetTransactionsAsync(accountId: bank.Value)).Count);
        Assert.Equal(2, (await transactions.GetTransactionsAsync(categoryId: food.Value)).Count);
        Assert.Single(await transactions.GetTransactionsAsync(minimumAmountMinor: 200_000));
        Assert.Equal(2, (await transactions.GetTransactionsAsync(maximumAmountMinor: 100_000)).Count);
        Assert.Single(await transactions.GetTransactionsAsync(
            fromDate: BusinessDate, accountId: bank.Value, categoryId: food.Value,
            minimumAmountMinor: 100_000, maximumAmountMinor: 100_000));
        var transfer = await transactions.CreateTransferAsync(
            new TransferRequest(BusinessDate, 25_000, bank.Value, wallet.Value, "Transfer"));
        Assert.True(transfer.IsSuccess);
        Assert.Equal(3, (await transactions.GetTransactionsAsync(fromDate: BusinessDate)).Count);
        Assert.Equal(3, (await transactions.GetTransactionsAsync(accountId: bank.Value)).Count);
        Assert.Equal(2, (await transactions.GetTransactionsAsync(categoryId: food.Value)).Count);
        Assert.Equal(olderTravel.Value, (await transactions.GetTransactionsAsync(sortOrder: TransactionSortOrder.OldestFirst))[0].Id);
        await Assert.ThrowsAsync<ArgumentException>(() => transactions.GetTransactionsAsync(
            fromDate: BusinessDate, toDate: BusinessDate.AddDays(-1)));
        await Assert.ThrowsAsync<ArgumentException>(() => transactions.GetTransactionsAsync(
            minimumAmountMinor: 200_000, maximumAmountMinor: 100_000));
        await Assert.ThrowsAsync<ArgumentException>(() => transactions.GetTransactionsAsync(minimumAmountMinor: -1));
    }

    [Fact]
    public async Task GetTransactionsAsync_LoadsFiftyThousandTransactions()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDatabase(connection);
        await db.Database.MigrateAsync();

        for (var batch = 0; batch < 50; batch++)
        {
            var transactions = Enumerable.Range(0, 1_000)
                .Select(index => new DbLedgerTransaction
                {
                    BusinessDate = BusinessDate.AddDays(-(batch * 1_000 + index) % 365),
                    Kind = TransactionKind.Expense,
                    Description = $"Transaction {batch * 1_000 + index}",
                    Source = TransactionSource.Manual,
                    IsConfirmed = true,
                    CreatedAtUtc = NowUtc,
                    UpdatedAtUtc = NowUtc
                });
            db.Transactions.AddRange(transactions);
            await db.SaveChangesAsync();
        }

        var service = new TransactionService(new EfAccountingRepository(db), new FixedClock(NowUtc));
        var results = await service.GetTransactionsAsync(take: 50_000);

        Assert.Equal(50_000, results.Count);
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
