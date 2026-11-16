using Hesabyar.Application.Sms;
using Hesabyar.Domain.Accounts;
using Hesabyar.Domain.Ledger;
using Hesabyar.Infrastructure.Persistence;
using Hesabyar.Infrastructure.Persistence.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Hesabyar.Infrastructure.Tests;

public sealed class BankSmsInboxTests
{
    [Fact]
    public async Task Import_IsIdempotent_AndConfirmationCreatesBalancedSmsLedgerTransaction()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using (var setup = new AppDbContext(options))
        {
            await setup.Database.MigrateAsync();
            setup.Accounts.AddRange(
                new DbAccount { Name = "Bank", Type = AccountType.Asset, Kind = AccountKind.Bank, CurrencyCode = "IRR" },
                new DbAccount { Name = "Food", Type = AccountType.Expense, Kind = AccountKind.Category, CurrencyCode = "IRR" });
            await setup.SaveChangesAsync();
        }

        var service = new BankSmsInboxService(new TestDbContextFactory(options),
            new BankSmsParser([new BankSmsRule("test", ["TestBank"], ["برداشت"], ["واریز"])]),
            TimeProvider.System);
        const string body = "برداشت مبلغ: ۱۲۵۰۰۰ ریال";
        Assert.Equal(SmsImportOutcome.Added, await service.ImportAsync("TestBank", body));
        Assert.Equal(SmsImportOutcome.Duplicate, await service.ImportAsync("TestBank", body));

        var item = Assert.Single(await service.GetPendingAsync());
        Assert.Equal(BankSmsTransactionType.Expense, item.TransactionType);
        Assert.Equal(125_000, item.AmountMinor);
        Assert.True(item.AmountUnitKnown);

        Guid bankId;
        Guid categoryId;
        await using (var db = new AppDbContext(options))
        {
            bankId = await db.Accounts.Where(x => x.Name == "Bank").Select(x => x.Id).SingleAsync();
            categoryId = await db.Accounts.Where(x => x.Name == "Food").Select(x => x.Id).SingleAsync();
        }

        Assert.True(await service.ConfirmAsync(item.Id, bankId, categoryId,
            BankSmsTransactionType.Expense, 125_000, new DateOnly(2026, 11, 15)));
        Assert.Empty(await service.GetPendingAsync());

        await using var verify = new AppDbContext(options);
        var transaction = await verify.Transactions.Include(x => x.Postings).SingleAsync();
        Assert.Equal(TransactionSource.Sms, transaction.Source);
        Assert.True(transaction.IsConfirmed);
        Assert.False(string.IsNullOrWhiteSpace(transaction.SmsMessageId));
        Assert.Equal(-125_000, transaction.Postings.Single(x => x.AccountId == bankId).AmountMinor);
        Assert.Equal(125_000, transaction.Postings.Single(x => x.AccountId == categoryId).AmountMinor);
        Assert.Equal(0, transaction.Postings.Sum(x => x.AmountMinor));
    }

    [Fact]
    public async Task Ignore_RemovesItemFromReviewQueue_AndPreventsReimport()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync();

        var service = new BankSmsInboxService(new TestDbContextFactory(options), new BankSmsParser(), TimeProvider.System);
        Assert.Equal(SmsImportOutcome.Added, await service.ImportAsync("sender", "برداشت مبلغ: ۱۰۰۰"));
        var item = Assert.Single(await service.GetPendingAsync());
        Assert.True(await service.IgnoreAsync(item.Id));
        Assert.Empty(await service.GetPendingAsync());
        Assert.Equal(SmsImportOutcome.Duplicate, await service.ImportAsync("sender", "برداشت مبلغ: ۱۰۰۰"));
    }


    [Fact]
    public async Task DetectsManualTransactionWithSameDateAccountsAndAmount()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        Guid bankId;
        Guid categoryId;
        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
            var bank = new DbAccount { Name = "Bank", Type = AccountType.Asset, Kind = AccountKind.Bank, CurrencyCode = "IRR" };
            var food = new DbAccount { Name = "Food", Type = AccountType.Expense, Kind = AccountKind.Category, CurrencyCode = "IRR" };
            var manual = new DbLedgerTransaction
            {
                BusinessDate = new DateOnly(2026, 11, 15),
                Kind = TransactionKind.Expense,
                Source = TransactionSource.Manual,
                IsConfirmed = true
            };
            manual.Postings.Add(new DbPosting { Account = food, AmountMinor = 75_000, CurrencyCode = "IRR" });
            manual.Postings.Add(new DbPosting { Account = bank, AmountMinor = -75_000, CurrencyCode = "IRR" });
            db.Transactions.Add(manual);
            await db.SaveChangesAsync();
            bankId = bank.Id;
            categoryId = food.Id;
        }

        var service = new BankSmsInboxService(new TestDbContextFactory(options), new BankSmsParser(), TimeProvider.System);
        Assert.True(await service.HasPossibleManualDuplicateAsync(
            bankId, categoryId, BankSmsTransactionType.Expense, 75_000, new DateOnly(2026, 11, 15)));
        Assert.False(await service.HasPossibleManualDuplicateAsync(
            bankId, categoryId, BankSmsTransactionType.Expense, 80_000, new DateOnly(2026, 11, 15)));
    }


    [Fact]
    public async Task KnownSmsBalanceCanCreateBalancedAdjustmentTransaction()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        Guid bankId;
        Guid categoryId;
        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
            var bank = new DbAccount { Name = "Bank", Type = AccountType.Asset, Kind = AccountKind.Bank, CurrencyCode = "IRR" };
            var food = new DbAccount { Name = "Food", Type = AccountType.Expense, Kind = AccountKind.Category, CurrencyCode = "IRR" };
            db.Accounts.AddRange(bank, food);
            await db.SaveChangesAsync();
            bankId = bank.Id;
            categoryId = food.Id;
        }

        var service = new BankSmsInboxService(new TestDbContextFactory(options), new BankSmsParser(), TimeProvider.System);
        Assert.Equal(SmsImportOutcome.Added,
            await service.ImportAsync("sender", "برداشت مبلغ: ۵۰۰۰ ریال مانده: ۱۰۰۰۰۰ ریال"));
        var item = Assert.Single(await service.GetPendingAsync());
        Assert.True(item.AmountUnitKnown);
        Assert.True(await service.ConfirmAsync(item.Id, bankId, categoryId,
            BankSmsTransactionType.Expense, 5_000, new DateOnly(2026, 11, 15)));

        var mismatch = await service.GetBalanceReconciliationAsync(item.Id);
        Assert.NotNull(mismatch);
        Assert.Equal(105_000, mismatch.DifferenceMinor);
        Assert.True(await service.CreateBalanceAdjustmentAsync(item.Id));

        var reconciled = await service.GetBalanceReconciliationAsync(item.Id);
        Assert.NotNull(reconciled);
        Assert.Equal(100_000, reconciled.LedgerBalanceMinor);
        Assert.Equal(0, reconciled.DifferenceMinor);

        await using var verify = new AppDbContext(options);
        var adjustment = await verify.Transactions
            .Where(x => x.Kind == TransactionKind.Adjustment)
            .Include(x => x.Postings)
            .SingleAsync();
        Assert.Equal(TransactionSource.Sms, adjustment.Source);
        Assert.Equal(0, adjustment.Postings.Sum(x => x.AmountMinor));
        Assert.Equal(105_000, adjustment.Postings.Single(x => x.AccountId == bankId).AmountMinor);
        var systemAccount = await verify.Accounts.SingleAsync(x => x.SystemKey == "sms-balance-adjustment");
        Assert.False(systemAccount.IncludeInTotal);
    }


    [Fact]
    public async Task VerificationCodeSmsIsIgnoredWithoutPersistingIt()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync();

        var service = new BankSmsInboxService(
            new TestDbContextFactory(options), new BankSmsParser(), TimeProvider.System);

        Assert.Equal(SmsImportOutcome.Ignored,
            await service.ImportAsync("sender", "پرداخت قبض تلفن ثابت مبلغ 4,139,000 رمز: 00000000"));
        Assert.Empty(await service.GetPendingAsync());

        await using var verify = new AppDbContext(options);
        Assert.Empty(await verify.SmsInboxItems.ToListAsync());
    }

    [Fact]
    public async Task ImportDoesNotPersistUnrelatedPersonalMessages()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync();

        var service = new BankSmsInboxService(new TestDbContextFactory(options), new BankSmsParser(), TimeProvider.System);
        Assert.Equal(SmsImportOutcome.Ignored,
            await service.ImportAsync("sender", "سلام، جلسه فردا ساعت ۱۰ برگزار می‌شود."));

        await using var verify = new AppDbContext(options);
        Assert.Empty(await verify.SmsInboxItems.ToListAsync());
    }

    [Fact]
    public async Task BatchConfirmationImportsAllSelectedMessagesAtomically()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        Guid bankId;
        Guid categoryId;
        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
            var bank = new DbAccount { Name = "Bank", Type = AccountType.Asset, Kind = AccountKind.Bank, CurrencyCode = "IRR" };
            var food = new DbAccount { Name = "Food", Type = AccountType.Expense, Kind = AccountKind.Category, CurrencyCode = "IRR" };
            db.Accounts.AddRange(bank, food);
            await db.SaveChangesAsync();
            bankId = bank.Id;
            categoryId = food.Id;
        }

        var service = new BankSmsInboxService(new TestDbContextFactory(options), new BankSmsParser(), TimeProvider.System);
        Assert.Equal(SmsImportOutcome.Added, await service.ImportAsync("bank", "برداشت مبلغ: ۱۰۰۰ ریال"));
        Assert.Equal(SmsImportOutcome.Added, await service.ImportAsync("bank", "برداشت مبلغ: ۲۰۰۰ ریال"));
        var pending = await service.GetPendingAsync();
        var date = new DateOnly(2026, 11, 16);
        var confirmations = pending.Select(item => new SmsBatchConfirmation(
            item.Id, bankId, categoryId, BankSmsTransactionType.Expense, item.AmountMinor!.Value, date)).ToArray();

        Assert.True(await service.ConfirmBatchAsync(confirmations));
        Assert.Empty(await service.GetPendingAsync());

        await using var verify = new AppDbContext(options);
        var transactions = await verify.Transactions.Include(x => x.Postings).ToListAsync();
        Assert.Equal(2, transactions.Count);
        Assert.All(transactions, item => Assert.Equal(0, item.Postings.Sum(posting => posting.AmountMinor)));
    }

    [Fact]
    public async Task BatchConfirmationRollsBackEveryItemWhenOneConfirmationIsInvalid()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        Guid bankId;
        Guid categoryId;
        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
            var bank = new DbAccount { Name = "Bank", Type = AccountType.Asset, Kind = AccountKind.Bank, CurrencyCode = "IRR" };
            var food = new DbAccount { Name = "Food", Type = AccountType.Expense, Kind = AccountKind.Category, CurrencyCode = "IRR" };
            db.Accounts.AddRange(bank, food);
            await db.SaveChangesAsync();
            bankId = bank.Id;
            categoryId = food.Id;
        }

        var service = new BankSmsInboxService(new TestDbContextFactory(options), new BankSmsParser(), TimeProvider.System);
        Assert.Equal(SmsImportOutcome.Added, await service.ImportAsync("bank", "برداشت مبلغ: ۱۰۰۰ ریال"));
        Assert.Equal(SmsImportOutcome.Added, await service.ImportAsync("bank", "برداشت مبلغ: ۲۰۰۰ ریال"));
        var pending = await service.GetPendingAsync();
        var date = new DateOnly(2026, 11, 16);
        var confirmations = pending.Select((item, index) => new SmsBatchConfirmation(
            item.Id, bankId, index == 0 ? categoryId : bankId,
            BankSmsTransactionType.Expense, item.AmountMinor!.Value, date)).ToArray();

        Assert.False(await service.ConfirmBatchAsync(confirmations));
        Assert.Equal(2, (await service.GetPendingAsync()).Count);

        await using var verify = new AppDbContext(options);
        Assert.Empty(await verify.Transactions.ToListAsync());
    }

    private sealed class TestDbContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AppDbContext(options));
    }
}
