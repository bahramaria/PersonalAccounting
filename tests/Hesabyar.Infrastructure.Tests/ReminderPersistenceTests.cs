using Hesabyar.Domain.Accounts;
using Hesabyar.Infrastructure.Persistence;
using Hesabyar.Infrastructure.Persistence.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Hesabyar.Infrastructure.Tests;

public sealed class ReminderPersistenceTests
{
    [Fact]
    public async Task Migration_PersistsRemindersAndRecurringSchedules()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync();

        var account = new DbAccount
        {
            Name = "Cash",
            Type = AccountType.Asset,
            Kind = AccountKind.Cash,
            CurrencyCode = "IRR"
        };
        db.Accounts.Add(account);
        db.Reminders.Add(new DbReminder
        {
            Title = "پرداخت قبض",
            Kind = "Bill",
            DueDate = new DateOnly(2026, 12, 1),
            IsCompleted = false
        });
        db.RecurringTransactions.Add(new DbRecurringTransaction
        {
            Title = "اجاره ماهانه",
            Kind = "Expense",
            CurrencyCode = "IRR",
            AmountMinor = 100_000_000,
            AccountId = account.Id,
            StartDate = new DateOnly(2026, 12, 1),
            NextOccurrenceDate = new DateOnly(2026, 12, 1),
            IntervalCount = 1,
            IntervalUnit = "Month"
        });

        await db.SaveChangesAsync();

        Assert.Equal("پرداخت قبض", await db.Reminders.Select(x => x.Title).SingleAsync());
        Assert.Equal(new DateOnly(2026, 12, 1),
            await db.RecurringTransactions.Select(x => x.NextOccurrenceDate).SingleAsync());
    }

    [Fact]
    public async Task RecurringOccurrence_RejectsDuplicateScheduleDate()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync();

        var account = new DbAccount
        {
            Name = "Cash",
            Type = AccountType.Asset,
            Kind = AccountKind.Cash,
            CurrencyCode = "IRR"
        };
        var schedule = new DbRecurringTransaction
        {
            Title = "Monthly rent",
            Kind = "Expense",
            CurrencyCode = "IRR",
            AmountMinor = 1000,
            AccountId = account.Id,
            StartDate = new DateOnly(2026, 12, 1),
            NextOccurrenceDate = new DateOnly(2026, 12, 1),
            IntervalCount = 1,
            IntervalUnit = "Month"
        };
        db.Accounts.Add(account);
        db.RecurringTransactions.Add(schedule);
        await db.SaveChangesAsync();

        db.RecurringTransactionOccurrences.AddRange(
            new DbRecurringTransactionOccurrence
            {
                RecurringTransactionId = schedule.Id,
                OccurrenceDate = new DateOnly(2026, 12, 1),
                Status = "Pending"
            },
            new DbRecurringTransactionOccurrence
            {
                RecurringTransactionId = schedule.Id,
                OccurrenceDate = new DateOnly(2026, 12, 1),
                Status = "Pending"
            });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}