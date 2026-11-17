using Hesabyar.Domain.Accounts;
using Hesabyar.Domain.Ledger;
using Hesabyar.Infrastructure.Persistence;
using Hesabyar.Infrastructure.Persistence.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Hesabyar.Infrastructure.Tests;

public sealed class ReportsServiceTests
{
    [Fact]
    public async Task GetAsync_AggregatesExpensesIncomeAndPersianMonths()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using (var db = new AppDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            var cash = new DbAccount { Name = "Cash", Type = AccountType.Asset, Kind = AccountKind.Cash, CurrencyCode = "IRR" };
            var food = new DbAccount { Name = "Food", Type = AccountType.Expense, Kind = AccountKind.Category, CurrencyCode = "IRR" };
            var foodDuplicate = new DbAccount { Name = "Food", Type = AccountType.Expense, Kind = AccountKind.Category, CurrencyCode = "IRR" };
            var salary = new DbAccount { Name = "Salary", Type = AccountType.Income, Kind = AccountKind.Category, CurrencyCode = "IRR" };
            var clearing = new DbAccount { Name = "People clearing", Type = AccountType.Asset, Kind = AccountKind.System,
                CurrencyCode = "IRR", SystemKey = "people-clearing", IncludeInTotal = false };
            var person = new DbParty { Name = "Ali" };
            db.Accounts.AddRange(cash, food, foodDuplicate, salary, clearing);
            db.Parties.Add(person);
            var firstDay = new DateOnly(2026, 3, 21);
            db.Transactions.AddRange(
                new DbLedgerTransaction
                {
                    BusinessDate = firstDay, Kind = TransactionKind.Expense, Source = TransactionSource.Manual,
                    IsConfirmed = true, Description = "Groceries",
                    Postings =
                    [
                        new DbPosting { Account = food, AmountMinor = 125_000, CurrencyCode = "IRR" },
                        new DbPosting { Account = cash, AmountMinor = -125_000, CurrencyCode = "IRR" }
                    ]
                },
                new DbLedgerTransaction
                {
                    BusinessDate = firstDay.AddDays(1), Kind = TransactionKind.Income, Source = TransactionSource.Manual,
                    IsConfirmed = true, Description = "Salary",
                    Postings =
                    [
                        new DbPosting { Account = cash, AmountMinor = 500_000, CurrencyCode = "IRR" },
                        new DbPosting { Account = salary, AmountMinor = -500_000, CurrencyCode = "IRR" }
                    ]
                },
                new DbLedgerTransaction
                {
                    BusinessDate = firstDay.AddDays(2), Kind = TransactionKind.Expense, Source = TransactionSource.Sms,
                    IsConfirmed = false, Description = "Pending",
                    Postings =
                    [
                        new DbPosting { Account = food, AmountMinor = 900_000, CurrencyCode = "IRR" },
                        new DbPosting { Account = cash, AmountMinor = -900_000, CurrencyCode = "IRR" }
                    ]
                },
                new DbLedgerTransaction
                {
                    BusinessDate = firstDay.AddDays(3), Kind = TransactionKind.Expense, Source = TransactionSource.Manual,
                    IsConfirmed = true, Description = "Food purchase",
                    Postings =
                    [
                        new DbPosting { Account = foodDuplicate, AmountMinor = 25_000, CurrencyCode = "IRR" },
                        new DbPosting { Account = cash, AmountMinor = -25_000, CurrencyCode = "IRR" }
                    ]
                },
                new DbLedgerTransaction
                {
                    BusinessDate = firstDay.AddDays(4), Kind = TransactionKind.PersonDebt, Source = TransactionSource.Manual,
                    IsConfirmed = true, PartyId = person.Id, Description = "Lent to Ali",
                    Postings =
                    [
                        new DbPosting { Account = clearing, AmountMinor = 50_000, CurrencyCode = "IRR", PartyId = person.Id },
                        new DbPosting { Account = cash, AmountMinor = -50_000, CurrencyCode = "IRR" }
                    ]
                });
            await db.SaveChangesAsync();
        }

        var factory = new TestContextFactory(options);
        var service = new ReportsService(factory);
        var report = await service.GetAsync(new DateOnly(2026, 3, 21), new DateOnly(2026, 4, 20));

        Assert.Equal(500_000, report.IncomeMinor);
        Assert.Equal(150_000, report.ExpenseMinor);
        Assert.Equal(2, report.ExpensesByCategory.Count);
        Assert.Equal(new long[] { 125_000, 25_000 }, report.ExpensesByCategory.Select(x => x.AmountMinor).ToArray());
        Assert.Single(report.MonthlyTrend);
        Assert.Equal("1405/01", report.MonthlyTrend[0].Label);
        Assert.Equal(500_000, report.MonthlyTrend[0].IncomeMinor);
        Assert.Equal(150_000, report.MonthlyTrend[0].ExpenseMinor);
        Assert.Equal(350_000, report.AssetsMinor);
        Assert.Equal(350_000, report.NetWorthMinor);
        var personReport = Assert.Single(report.People);
        Assert.Equal(50_000, personReport.BalanceMinor);
        Assert.Equal(50_000, personReport.PeriodChangeMinor);
        Assert.Equal(1, personReport.ActivityCount);
    }

    [Fact]
    public async Task GetAsync_FiltersAllTotalsAndRowsBySelectedCurrency()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using (var db = new AppDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            var usdCash = new DbAccount { Name = "USD wallet", Type = AccountType.Asset, Kind = AccountKind.Wallet, CurrencyCode = "USD" };
            var usdFood = new DbAccount { Name = "USD food", Type = AccountType.Expense, Kind = AccountKind.Category, CurrencyCode = "USD" };
            var usdSalary = new DbAccount { Name = "USD salary", Type = AccountType.Income, Kind = AccountKind.Category, CurrencyCode = "USD" };
            var irrCash = new DbAccount { Name = "IRR wallet", Type = AccountType.Asset, Kind = AccountKind.Wallet, CurrencyCode = "IRR" };
            var irrFood = new DbAccount { Name = "IRR food", Type = AccountType.Expense, Kind = AccountKind.Category, CurrencyCode = "IRR" };
            db.Accounts.AddRange(usdCash, usdFood, usdSalary, irrCash, irrFood);
            var date = new DateOnly(2026, 11, 17);
            db.Transactions.AddRange(
                new DbLedgerTransaction
                {
                    BusinessDate = date, Kind = TransactionKind.Expense, Source = TransactionSource.Manual,
                    IsConfirmed = true, Description = "USD expense",
                    Postings =
                    [
                        new DbPosting { Account = usdFood, AmountMinor = 25, CurrencyCode = "USD" },
                        new DbPosting { Account = usdCash, AmountMinor = -25, CurrencyCode = "USD" }
                    ]
                },
                new DbLedgerTransaction
                {
                    BusinessDate = date, Kind = TransactionKind.Income, Source = TransactionSource.Manual,
                    IsConfirmed = true, Description = "USD income",
                    Postings =
                    [
                        new DbPosting { Account = usdCash, AmountMinor = 150, CurrencyCode = "USD" },
                        new DbPosting { Account = usdSalary, AmountMinor = -150, CurrencyCode = "USD" }
                    ]
                },
                new DbLedgerTransaction
                {
                    BusinessDate = date, Kind = TransactionKind.Expense, Source = TransactionSource.Manual,
                    IsConfirmed = true, Description = "IRR expense",
                    Postings =
                    [
                        new DbPosting { Account = irrFood, AmountMinor = 900_000, CurrencyCode = "IRR" },
                        new DbPosting { Account = irrCash, AmountMinor = -900_000, CurrencyCode = "IRR" }
                    ]
                });
            await db.SaveChangesAsync();
        }

        var service = new ReportsService(new TestContextFactory(options));
        var usd = await service.GetAsync(new DateOnly(2026, 11, 17), new DateOnly(2026, 11, 17), currencyCode: "USD");
        var irr = await service.GetAsync(new DateOnly(2026, 11, 17), new DateOnly(2026, 11, 17));

        Assert.Equal("USD", usd.CurrencyCode);
        Assert.Equal(150, usd.IncomeMinor);
        Assert.Equal(25, usd.ExpenseMinor);
        Assert.Equal(125, usd.AssetsMinor);
        Assert.DoesNotContain(usd.Accounts, x => x.Name.Contains("IRR", StringComparison.Ordinal));
        Assert.Equal(900_000, irr.ExpenseMinor);
        var currencies = await service.GetCurrenciesAsync();
        Assert.Contains("USD", currencies);
        Assert.Contains("IRR", currencies);
    }

    [Fact]
    public async Task GetAsync_RejectsReversedDateRange()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        var service = new ReportsService(new TestContextFactory(options));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.GetAsync(new DateOnly(2026, 4, 2), new DateOnly(2026, 4, 1)));
    }

    private sealed class TestContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AppDbContext(options));
    }
}
