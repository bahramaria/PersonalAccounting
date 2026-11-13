using Hesabyar.Domain.Accounts;
using Hesabyar.Domain.Ledger;
using Hesabyar.Infrastructure.Persistence;
using Hesabyar.Infrastructure.Persistence.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Hesabyar.Infrastructure.Tests;

public sealed class AppDbContextTests
{
    [Fact]
    public async Task SaveChanges_PersistsAccountTransactionAndPostings()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync();

        var cash = new DbAccount { Name = "Cash", Type = AccountType.Asset, Kind = AccountKind.Cash, CurrencyCode = "IRR" };
        var food = new DbAccount { Name = "Food", Type = AccountType.Expense, Kind = AccountKind.Category, CurrencyCode = "IRR" };
        var transaction = new DbLedgerTransaction
        {
            BusinessDate = new DateOnly(2026, 11, 13),
            Kind = TransactionKind.Expense,
            Source = TransactionSource.Manual,
            IsConfirmed = true
        };
        transaction.Postings.Add(new DbPosting { Account = food, AmountMinor = 125_000, CurrencyCode = "IRR" });
        transaction.Postings.Add(new DbPosting { Account = cash, AmountMinor = -125_000, CurrencyCode = "IRR" });
        db.Transactions.Add(transaction);

        await db.SaveChangesAsync();

        Assert.Equal(2, await db.Postings.CountAsync());
        Assert.Equal(125_000, await db.Postings.Where(x => x.AmountMinor > 0).Select(x => x.AmountMinor).SingleAsync());
        Assert.Equal(new DateOnly(2026, 11, 13), await db.Transactions.Select(x => x.BusinessDate).SingleAsync());
    }

    [Fact]
    public async Task QueryFilter_ExcludesSoftDeletedAccounts()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync();
        db.Accounts.Add(new DbAccount
        {
            Name = "Archived", Type = AccountType.Asset, Kind = AccountKind.Cash,
            CurrencyCode = "IRR", IsDeleted = true
        });
        await db.SaveChangesAsync();

        Assert.Empty(await db.Accounts.ToListAsync());
        Assert.Single(await db.Accounts.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Database_RejectsZeroAmountPosting()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync();

        var account = new DbAccount { Name = "Cash", Type = AccountType.Asset, Kind = AccountKind.Cash, CurrencyCode = "IRR" };
        var transaction = new DbLedgerTransaction
        {
            BusinessDate = new DateOnly(2026, 11, 13),
            Kind = TransactionKind.Expense,
            Source = TransactionSource.Manual
        };
        transaction.Postings.Add(new DbPosting { Account = account, AmountMinor = 0, CurrencyCode = "IRR" });
        db.Transactions.Add(transaction);

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
