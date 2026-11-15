using Hesabyar.Application.Accounts;
using Hesabyar.Application.Common;
using Hesabyar.Application.People;
using Hesabyar.Domain.Accounts;
using Hesabyar.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Hesabyar.Infrastructure.Tests;

public sealed class PeopleServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 15);

    [Fact]
    public async Task DebtAndPartialRepayment_UpdatePersonBalanceAndLedger()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync();
        var accounting = new AccountService(new EfAccountingRepository(db), new FixedClock());
        var account = await accounting.CreateAsync(new CreateAccountRequest("Bank", AccountType.Asset, AccountKind.Bank));
        var service = new PersonService(new PeopleRepository(db), new FixedClock());
        var person = await service.CreateAsync(new CreatePersonRequest("Ali", "09120000000"));
        Assert.True(account.IsSuccess);
        Assert.True(person.IsSuccess);

        var dueDate = Today.AddDays(20);
        var debt = await service.AddDebtAsync(person.Value, account.Value, 1_000_000, true, Today, "Loan", dueDate);
        var repayment = await service.RepayAsync(person.Value, account.Value, 350_000, Today, "Partial repayment");
        var balance = Assert.Single(await service.GetPeopleAsync());

        Assert.True(debt.IsSuccess);
        Assert.True(repayment.IsSuccess);
        Assert.Equal(650_000, balance.BalanceMinor);
        Assert.Equal(2, (await service.GetEntriesAsync(person.Value)).Count);
        var openDues = await service.GetOutstandingDuesAsync(person.Value);
        Assert.Single(openDues);
        Assert.Equal(dueDate, openDues[0].DueDate);
        Assert.Equal(650_000, openDues[0].RemainingAmountMinor);
        Assert.Equal(2, await db.Transactions.CountAsync());
        Assert.Equal(4, await db.Postings.CountAsync());
        Assert.All(await db.Transactions.ToListAsync(), transaction => Assert.Equal(person.Value, transaction.PartyId));
        Assert.True((await service.RepayAsync(person.Value, account.Value, 650_000, Today, "Final repayment")).IsSuccess);
        Assert.Empty(await service.GetOutstandingDuesAsync(person.Value));
        Assert.Equal(0, Assert.Single(await service.GetPeopleAsync()).BalanceMinor);
    }

    [Fact]
    public async Task BorrowAndRepayment_UseOppositeBalanceDirection()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync();
        var accounts = new AccountService(new EfAccountingRepository(db), new FixedClock());
        var account = await accounts.CreateAsync(new CreateAccountRequest("Cash", AccountType.Asset, AccountKind.Cash));
        var service = new PersonService(new PeopleRepository(db), new FixedClock());
        var person = await service.CreateAsync(new CreatePersonRequest("Sara"));

        Assert.True((await service.AddDebtAsync(person.Value, account.Value, 500_000, false, Today, null)).IsSuccess);
        Assert.Equal(-500_000, Assert.Single(await service.GetPeopleAsync()).BalanceMinor);
        Assert.True((await service.RepayAsync(person.Value, account.Value, 500_000, Today, null)).IsSuccess);
        Assert.Equal(0, Assert.Single(await service.GetPeopleAsync()).BalanceMinor);
    }

    [Fact]
    public async Task RepaymentCannotExceedBalance_AndPersonWithBalanceCannotBeArchived()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync();
        var accounts = new AccountService(new EfAccountingRepository(db), new FixedClock());
        var account = await accounts.CreateAsync(new CreateAccountRequest("Bank", AccountType.Asset, AccountKind.Bank));
        var service = new PersonService(new PeopleRepository(db), new FixedClock());
        var person = await service.CreateAsync(new CreatePersonRequest("Reza"));
        await service.AddDebtAsync(person.Value, account.Value, 100_000, true, Today, null);

        var excessive = await service.RepayAsync(person.Value, account.Value, 100_001, Today, null);
        var archive = await service.ArchiveAsync(person.Value);

        Assert.True(excessive.IsFailure);
        Assert.Equal("person.repayment.invalid", excessive.Error.Code);
        Assert.True(archive.IsFailure);
        Assert.Equal(100_000, Assert.Single(await service.GetPeopleAsync()).BalanceMinor);
    }

    [Fact]
    public async Task ConfirmedOverpayment_CreatesOppositeDebtAtomically()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync();
        var accounts = new AccountService(new EfAccountingRepository(db), new FixedClock());
        var account = await accounts.CreateAsync(new CreateAccountRequest("Bank", AccountType.Asset, AccountKind.Bank));
        var service = new PersonService(new PeopleRepository(db), new FixedClock());
        var person = await service.CreateAsync(new CreatePersonRequest("Nima"));
        await service.AddDebtAsync(person.Value, account.Value, 100_000, true, Today, null);

        var result = await service.RepayAsync(person.Value, account.Value, 130_000, Today, null,
            allowOverpayment: true);
        var balance = Assert.Single(await service.GetPeopleAsync());

        Assert.True(result.IsSuccess);
        Assert.Equal(-30_000, balance.BalanceMinor);
        Assert.Equal(3, await db.Transactions.CountAsync());
        Assert.Equal(6, await db.Postings.CountAsync());
        Assert.Equal(0L, await db.Transactions.Where(x => x.PartyId == person.Value)
            .SelectMany(x => x.Postings).SumAsync(x => x.AmountMinor));
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }
}
