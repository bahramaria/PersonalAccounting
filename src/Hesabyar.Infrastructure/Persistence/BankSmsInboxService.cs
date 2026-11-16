using Hesabyar.Application.Sms;
using Hesabyar.Domain.Accounts;
using Hesabyar.Domain.Ledger;
using Hesabyar.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;

namespace Hesabyar.Infrastructure.Persistence;

public enum SmsInboxStatus { Pending, Imported, Ignored, Duplicate }
public enum SmsImportOutcome { Added, Duplicate, Ignored }

public sealed record SmsBalanceReconciliation(long SmsBalanceMinor, long LedgerBalanceMinor, long DifferenceMinor);

public sealed record SmsBatchConfirmation(Guid InboxItemId, Guid AccountId, Guid CategoryId,
    BankSmsTransactionType TransactionType, long AmountMinor, DateOnly BusinessDate);

public sealed record BankSmsInboxRecord(
    Guid Id, string Sender, string Body, string? BankKey,
    BankSmsParseStatus ParseStatus, BankSmsTransactionType TransactionType,
    long? AmountMinor, bool AmountUnitKnown, long? BalanceMinor, string? CardLastFour,
    string? DateText, string? TransactionTime, SmsInboxStatus Status,
    Guid? TransactionId, DateTimeOffset CreatedAtUtc);

public sealed class BankSmsInboxService(
    IDbContextFactory<AppDbContext> contextFactory,
    BankSmsParser parser,
    TimeProvider timeProvider)
{
    public async Task<SmsImportOutcome> ImportAsync(string? sender, string body, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        if (body.Length > 4000 || sender?.Length > 120)
            throw new ArgumentException("SMS content exceeds the supported length.", nameof(body));

        var safeSender = BankSmsParser.Normalize(sender ?? string.Empty);
        var safeBody = BankSmsParser.Normalize(body);
        var parsed = parser.Parse(safeSender, safeBody);
        if (parsed.Status == BankSmsParseStatus.Unsupported)
            return SmsImportOutcome.Ignored;
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        if (await db.SmsInboxItems.AnyAsync(x => x.DedupeHash == parsed.DedupeHash, cancellationToken) ||
            await db.Transactions.AnyAsync(x => x.SmsMessageId == parsed.DedupeHash, cancellationToken))
            return SmsImportOutcome.Duplicate;

        db.SmsInboxItems.Add(new DbSmsInboxItem
        {
            Sender = safeSender,
            Body = safeBody,
            DedupeHash = parsed.DedupeHash,
            BankKey = parsed.BankKey,
            ParseStatus = parsed.Status.ToString(),
            TransactionType = parsed.TransactionType.ToString(),
            AmountMinor = parsed.AmountMinor,
            AmountUnitKnown = parsed.AmountUnitKnown,
            BalanceMinor = parsed.BalanceMinor,
            CardLastFour = parsed.CardLastFour,
            DateText = parsed.DateText,
            TransactionTime = parsed.TransactionTime?.ToString("HH:mm"),
            Status = SmsInboxStatus.Pending.ToString()
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return SmsImportOutcome.Added;
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqliteException { SqliteErrorCode: 19 })
        {
            return SmsImportOutcome.Duplicate;
        }
    }

    public async Task<IReadOnlyList<BankSmsInboxRecord>> GetPendingAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var items = await db.SmsInboxItems.AsNoTracking()
            .Where(x => x.Status == SmsInboxStatus.Pending.ToString())
            .OrderByDescending(x => x.Id)
            .ToListAsync(cancellationToken);
        return items.Select(ToRecord).ToArray();
    }

    public async Task<bool> ConfirmAsync(
        Guid inboxItemId, Guid accountId, Guid categoryId,
        BankSmsTransactionType transactionType, long amountMinor,
        DateOnly businessDate, CancellationToken cancellationToken = default)
    {
        if (amountMinor <= 0 || transactionType is not (BankSmsTransactionType.Expense or BankSmsTransactionType.Income))
            return false;

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var item = await db.SmsInboxItems.SingleOrDefaultAsync(
            x => x.Id == inboxItemId && x.Status == SmsInboxStatus.Pending.ToString(), cancellationToken);
        if (item is null) return false;

        if (await db.Transactions.AnyAsync(x => x.SmsMessageId == item.DedupeHash, cancellationToken))
        {
            item.Status = SmsInboxStatus.Duplicate.ToString();
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        var account = await db.Accounts.SingleOrDefaultAsync(x => x.Id == accountId && !x.IsArchived, cancellationToken);
        var category = await db.Accounts.SingleOrDefaultAsync(x => x.Id == categoryId && !x.IsArchived, cancellationToken);
        if (account is null || category is null || category.Kind != AccountKind.Category)
            return false;

        var accountValid = transactionType == BankSmsTransactionType.Income
            ? account.Type == AccountType.Asset
            : account.Type is AccountType.Asset or AccountType.Liability;
        var categoryValid = transactionType == BankSmsTransactionType.Income
            ? category.Type == AccountType.Income
            : category.Type == AccountType.Expense;
        if (!accountValid || !categoryValid ||
            !string.Equals(account.CurrencyCode, category.CurrencyCode, StringComparison.Ordinal))
            return false;

        var now = timeProvider.GetUtcNow();
        var ledger = new DbLedgerTransaction
        {
            BusinessDate = businessDate,
            Kind = transactionType == BankSmsTransactionType.Income ? TransactionKind.Income : TransactionKind.Expense,
            Description = string.IsNullOrWhiteSpace(item.Sender) ? "SMS import" : $"SMS · {item.Sender}",
            Source = TransactionSource.Sms,
            SmsMessageId = item.DedupeHash,
            IsConfirmed = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        var accountAmount = transactionType == BankSmsTransactionType.Income ? amountMinor : -amountMinor;
        ledger.Postings.Add(new DbPosting
        {
            AccountId = account.Id, AmountMinor = accountAmount, CurrencyCode = account.CurrencyCode,
            CreatedAtUtc = now, UpdatedAtUtc = now
        });
        ledger.Postings.Add(new DbPosting
        {
            AccountId = category.Id, AmountMinor = -accountAmount, CurrencyCode = category.CurrencyCode,
            CreatedAtUtc = now, UpdatedAtUtc = now
        });

        db.Transactions.Add(ledger);
        item.Status = SmsInboxStatus.Imported.ToString();
        item.TransactionId = ledger.Id;
        item.UpdatedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }


    public async Task<bool> ConfirmBatchAsync(
        IReadOnlyCollection<SmsBatchConfirmation> confirmations,
        CancellationToken cancellationToken = default)
    {
        if (confirmations.Count == 0 || confirmations.Count > 100 ||
            confirmations.Any(x => x.AmountMinor <= 0 ||
                x.TransactionType is not (BankSmsTransactionType.Expense or BankSmsTransactionType.Income)))
            return false;

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var ids = confirmations.Select(x => x.InboxItemId).ToArray();
        if (ids.Distinct().Count() != ids.Length)
            return false;

        var items = await db.SmsInboxItems
            .Where(x => ids.Contains(x.Id) && x.Status == SmsInboxStatus.Pending.ToString())
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        if (items.Count != confirmations.Count)
            return false;

        var now = timeProvider.GetUtcNow();
        foreach (var confirmation in confirmations)
        {
            var item = items[confirmation.InboxItemId];
            if (await db.Transactions.AnyAsync(x => x.SmsMessageId == item.DedupeHash, cancellationToken))
                return false;

            var account = await db.Accounts.SingleOrDefaultAsync(
                x => x.Id == confirmation.AccountId && !x.IsArchived, cancellationToken);
            var category = await db.Accounts.SingleOrDefaultAsync(
                x => x.Id == confirmation.CategoryId && !x.IsArchived, cancellationToken);
            if (account is null || category is null || category.Kind != AccountKind.Category)
                return false;

            var isIncome = confirmation.TransactionType == BankSmsTransactionType.Income;
            var accountValid = isIncome ? account.Type == AccountType.Asset :
                account.Type is AccountType.Asset or AccountType.Liability;
            var categoryValid = isIncome ? category.Type == AccountType.Income : category.Type == AccountType.Expense;
            if (!accountValid || !categoryValid || account.CurrencyCode != category.CurrencyCode)
                return false;

            var accountAmount = isIncome ? confirmation.AmountMinor : -confirmation.AmountMinor;
            var ledger = new DbLedgerTransaction
            {
                BusinessDate = confirmation.BusinessDate,
                Kind = isIncome ? TransactionKind.Income : TransactionKind.Expense,
                Description = string.IsNullOrWhiteSpace(item.Sender) ? "SMS import" : $"SMS · {item.Sender}",
                Source = TransactionSource.Sms,
                SmsMessageId = item.DedupeHash,
                IsConfirmed = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            ledger.Postings.Add(new DbPosting
            {
                AccountId = account.Id, AmountMinor = accountAmount, CurrencyCode = account.CurrencyCode,
                CreatedAtUtc = now, UpdatedAtUtc = now
            });
            ledger.Postings.Add(new DbPosting
            {
                AccountId = category.Id, AmountMinor = -accountAmount, CurrencyCode = category.CurrencyCode,
                CreatedAtUtc = now, UpdatedAtUtc = now
            });
            db.Transactions.Add(ledger);
            item.Status = SmsInboxStatus.Imported.ToString();
            item.TransactionId = ledger.Id;
            item.UpdatedAtUtc = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> HasPossibleManualDuplicateAsync(
        Guid accountId, Guid categoryId, BankSmsTransactionType transactionType,
        long amountMinor, DateOnly businessDate, CancellationToken cancellationToken = default)
    {
        if (amountMinor <= 0 || transactionType is not (BankSmsTransactionType.Expense or BankSmsTransactionType.Income))
            return false;

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var kind = transactionType == BankSmsTransactionType.Income ? TransactionKind.Income : TransactionKind.Expense;
        var accountAmount = transactionType == BankSmsTransactionType.Income ? amountMinor : -amountMinor;
        return await db.Transactions.AnyAsync(x =>
            x.Source == TransactionSource.Manual &&
            x.Kind == kind &&
            x.BusinessDate == businessDate &&
            x.Postings.Any(p => p.AccountId == accountId && p.AmountMinor == accountAmount) &&
            x.Postings.Any(p => p.AccountId == categoryId && p.AmountMinor == -accountAmount),
            cancellationToken);
    }


    public async Task<SmsBalanceReconciliation?> GetBalanceReconciliationAsync(
        Guid inboxItemId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var item = await db.SmsInboxItems.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == inboxItemId && x.Status == SmsInboxStatus.Imported.ToString(), cancellationToken);
        if (item is null || !item.AmountUnitKnown || item.BalanceMinor is not long smsBalance ||
            item.TransactionId is not Guid transactionId)
            return null;

        var bankAccount = await (
            from posting in db.Postings
            join account in db.Accounts on posting.AccountId equals account.Id
            where posting.TransactionId == transactionId &&
                  account.Type == AccountType.Asset &&
                  account.Kind != AccountKind.Category
            select new { account.Id, account.CurrencyCode })
            .FirstOrDefaultAsync(cancellationToken);
        if (bankAccount is null || bankAccount.CurrencyCode != "IRR")
            return null;

        var ledgerBalance = await db.Postings
            .Where(x => x.AccountId == bankAccount.Id)
            .SumAsync(x => (long?)x.AmountMinor, cancellationToken) ?? 0L;
        return new SmsBalanceReconciliation(smsBalance, ledgerBalance, smsBalance - ledgerBalance);
    }

    public async Task<bool> CreateBalanceAdjustmentAsync(
        Guid inboxItemId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var item = await db.SmsInboxItems.SingleOrDefaultAsync(
            x => x.Id == inboxItemId && x.Status == SmsInboxStatus.Imported.ToString(), cancellationToken);
        if (item is null || item.BalanceAdjustmentTransactionId is not null ||
            !item.AmountUnitKnown || item.BalanceMinor is not long smsBalance ||
            item.TransactionId is not Guid transactionId)
            return false;

        var original = await db.Transactions.SingleOrDefaultAsync(x => x.Id == transactionId, cancellationToken);
        if (original is null)
            return false;

        var bankAccount = await (
            from posting in db.Postings
            join account in db.Accounts on posting.AccountId equals account.Id
            where posting.TransactionId == transactionId &&
                  account.Type == AccountType.Asset &&
                  account.Kind != AccountKind.Category
            select account)
            .FirstOrDefaultAsync(cancellationToken);
        if (bankAccount is null || bankAccount.CurrencyCode != "IRR")
            return false;

        var ledgerBalance = await db.Postings
            .Where(x => x.AccountId == bankAccount.Id)
            .SumAsync(x => (long?)x.AmountMinor, cancellationToken) ?? 0L;
        var difference = smsBalance - ledgerBalance;
        if (difference == 0)
            return false;

        var adjustmentAccount = await db.Accounts.SingleOrDefaultAsync(
            x => x.SystemKey == "sms-balance-adjustment" && x.CurrencyCode == "IRR", cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (adjustmentAccount is null)
        {
            adjustmentAccount = new DbAccount
            {
                Name = "تعدیل موجودی پیامک",
                Type = AccountType.Equity,
                Kind = AccountKind.System,
                CurrencyCode = "IRR",
                SystemKey = "sms-balance-adjustment",
                IncludeInTotal = false,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.Accounts.Add(adjustmentAccount);
        }

        var adjustment = new DbLedgerTransaction
        {
            BusinessDate = original.BusinessDate,
            Kind = TransactionKind.Adjustment,
            Description = "SMS balance reconciliation",
            Source = TransactionSource.Sms,
            IsConfirmed = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        adjustment.Postings.Add(new DbPosting
        {
            Account = bankAccount,
            AmountMinor = difference,
            CurrencyCode = bankAccount.CurrencyCode,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        adjustment.Postings.Add(new DbPosting
        {
            Account = adjustmentAccount,
            AmountMinor = -difference,
            CurrencyCode = "IRR",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        db.Transactions.Add(adjustment);
        item.BalanceAdjustmentTransactionId = adjustment.Id;
        item.UpdatedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> IgnoreAsync(Guid inboxItemId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var item = await db.SmsInboxItems.SingleOrDefaultAsync(
            x => x.Id == inboxItemId && x.Status == SmsInboxStatus.Pending.ToString(), cancellationToken);
        if (item is null) return false;
        item.Status = SmsInboxStatus.Ignored.ToString();
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static BankSmsInboxRecord ToRecord(DbSmsInboxItem item) =>
        new(item.Id, item.Sender, item.Body, item.BankKey,
            Enum.TryParse<BankSmsParseStatus>(item.ParseStatus, out var parse) ? parse : BankSmsParseStatus.NeedsReview,
            Enum.TryParse<BankSmsTransactionType>(item.TransactionType, out var type) ? type : BankSmsTransactionType.Unknown,
            item.AmountMinor, item.AmountUnitKnown, item.BalanceMinor, item.CardLastFour, item.DateText, item.TransactionTime,
            Enum.TryParse<SmsInboxStatus>(item.Status, out var status) ? status : SmsInboxStatus.Pending,
            item.TransactionId, item.CreatedAtUtc);
}
