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
            var salary = new DbAccount { Name = "Salary", Type = AccountType.Income, Kind = AccountKind.Category, CurrencyCode = "IRR" };
            db.Accounts.AddRange(cash, food, salary);
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
                });
            await db.SaveChangesAsync();
        }

        var factory = new TestContextFactory(options);
        var service = new ReportsService(factory);
        var report = await service.GetAsync(new DateOnly(2026, 3, 21), new DateOnly(2026, 4, 20));

        Assert.Equal(500_000, report.IncomeMinor);
        Assert.Equal(125_000, report.ExpenseMinor);
        Assert.Equal(125_000, Assert.Single(report.ExpensesByCategory).AmountMinor);
        Assert.Single(report.MonthlyTrend);
        Assert.Equal("1405/01", report.MonthlyTrend[0].Label);
        Assert.Equal(375_000, report.AssetsMinor);
        Assert.Equal(375_000, report.NetWorthMinor);
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
