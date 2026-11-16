using Hesabyar.Application.Common;
using Hesabyar.Application.Recurring;
using Hesabyar.Domain.Accounts;
using Hesabyar.Domain.Ledger;
using Hesabyar.Domain.ValueObjects;
using Hesabyar.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Hesabyar.Infrastructure.Persistence;

public sealed class RecurringTransactionService(
    IDbContextFactory<AppDbContext> contextFactory,
    IClock clock,
    RecurringOccurrenceScheduler scheduler)
{
    public async Task<Guid> CreateAsync(
        CreateRecurringScheduleRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var (account, category) = await ValidateAccountsAsync(db, request.Kind, request.AccountId,
            request.CategoryAccountId, request.CurrencyCode, cancellationToken);

        var schedule = new DbRecurringTransaction
        {
            Title = request.Title.Trim(),
            Kind = request.Kind,
            CurrencyCode = request.CurrencyCode,
            AmountMinor = request.AmountMinor,
            AccountId = account.Id,
            CategoryAccountId = category.Id,
            StartDate = request.StartDate,
            NextOccurrenceDate = request.StartDate,
            EndDate = request.EndDate,
            IntervalCount = request.IntervalCount,
            IntervalUnit = request.IntervalUnit,
            IsPaused = false
        };

        db.RecurringTransactions.Add(schedule);
        await db.SaveChangesAsync(cancellationToken);
        return schedule.Id;
    }

    public async Task<IReadOnlyList<RecurringScheduleListItem>> GetSchedulesAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.RecurringTransactions.AsNoTracking()
            .OrderBy(x => x.IsPaused)
            .ThenBy(x => x.NextOccurrenceDate)
            .Select(x => new RecurringScheduleListItem(
                x.Id, x.Title, x.Kind, x.CurrencyCode, x.AmountMinor,
                x.StartDate, x.NextOccurrenceDate, x.EndDate,
                x.IntervalCount, x.IntervalUnit, x.IsPaused))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RecurringAccountChoice>> GetAccountChoicesAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var accounts = await db.Accounts.AsNoTracking()
            .Where(x => !x.IsArchived && (x.Type == AccountType.Asset || x.Type == AccountType.Liability))
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return accounts
            .Select(x => new RecurringAccountChoice(x.Id, x.Name, x.CurrencyCode, x.Type.ToString()))
            .ToArray();
    }

    public async Task<IReadOnlyList<RecurringCategoryChoice>> GetCategoryChoicesAsync(
        string kind,
        CancellationToken cancellationToken = default)
    {
        var categoryType = ParseKind(kind) == TransactionKind.Expense ? AccountType.Expense : AccountType.Income;
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Accounts.AsNoTracking()
            .Where(x => !x.IsArchived && x.Kind == AccountKind.Category && x.Type == categoryType)
            .OrderBy(x => x.Name)
            .Select(x => new RecurringCategoryChoice(x.Id, x.Name, x.CurrencyCode))
            .ToListAsync(cancellationToken);
    }

    public Task<int> PrepareDueOccurrencesAsync(
        DateOnly throughDate,
        CancellationToken cancellationToken = default) =>
        scheduler.PrepareDueOccurrencesAsync(throughDate, cancellationToken);

    public async Task<IReadOnlyList<RecurringOccurrenceListItem>> GetOccurrencesAsync(
        Guid scheduleId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.RecurringTransactionOccurrences.AsNoTracking()
            .Where(x => x.RecurringTransactionId == scheduleId)
            .OrderByDescending(x => x.OccurrenceDate)
            .Select(x => new RecurringOccurrenceListItem(
                x.Id, x.OccurrenceDate, x.Status, x.TransactionId))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> SetPausedAsync(
        Guid scheduleId,
        bool paused,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var schedule = await db.RecurringTransactions.SingleOrDefaultAsync(x => x.Id == scheduleId, cancellationToken);
        if (schedule is null)
            return false;
        if (!paused && schedule.EndDate is not null &&
            schedule.EndDate.Value < DateOnly.FromDateTime(clock.UtcNow.LocalDateTime))
            return false;
        schedule.IsPaused = paused;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<Guid?> GenerateTransactionAsync(
        Guid occurrenceId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var occurrence = await db.RecurringTransactionOccurrences
            .SingleOrDefaultAsync(x => x.Id == occurrenceId, cancellationToken);
        if (occurrence is null)
            return null;
        if (occurrence.TransactionId is Guid existingTransactionId)
            return existingTransactionId;
        if (!string.Equals(occurrence.Status, "Pending", StringComparison.Ordinal))
            return null;

        var schedule = await db.RecurringTransactions
            .SingleOrDefaultAsync(x => x.Id == occurrence.RecurringTransactionId, cancellationToken);
        if (schedule is null)
            return null;

        var kind = ParseKind(schedule.Kind);
        DbAccount account;
        DbAccount category;
        try
        {
            (account, category) = await ValidateAccountsAsync(
                db, schedule.Kind, schedule.AccountId, schedule.CategoryAccountId,
                schedule.CurrencyCode, cancellationToken);
        }
        catch (ArgumentException)
        {
            return null;
        }

        var currency = new CurrencyCode(schedule.CurrencyCode);
        var amount = new Money(schedule.AmountMinor, currency);
        var postings = kind == TransactionKind.Expense
            ? new[] { new Posting(category.Id, amount), new Posting(account.Id, amount.Negate()) }
            : new[] { new Posting(account.Id, amount), new Posting(category.Id, amount.Negate()) };

        var nowUtc = clock.UtcNow;
        var ledgerTransaction = LedgerTransaction.Create(
            occurrence.OccurrenceDate, kind, postings, nowUtc, schedule.Title);

        var transactionEntity = new DbLedgerTransaction
        {
            Id = ledgerTransaction.Id,
            BusinessDate = ledgerTransaction.BusinessDate,
            Kind = ledgerTransaction.Kind,
            Description = ledgerTransaction.Description,
            Source = ledgerTransaction.Source,
            IsConfirmed = ledgerTransaction.IsConfirmed,
            CreatedAtUtc = ledgerTransaction.CreatedAtUtc,
            UpdatedAtUtc = ledgerTransaction.UpdatedAtUtc
        };
        foreach (var posting in ledgerTransaction.Postings)
        {
            transactionEntity.Postings.Add(new DbPosting
            {
                AccountId = posting.AccountId,
                AmountMinor = posting.Amount.Minor,
                CurrencyCode = posting.Amount.Currency.Value,
                PartyId = posting.PartyId,
                Memo = posting.Memo,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc
            });
        }

        db.Transactions.Add(transactionEntity);
        occurrence.TransactionId = ledgerTransaction.Id;
        occurrence.Status = "Generated";
        occurrence.GeneratedAtUtc = nowUtc;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ledgerTransaction.Id;
    }

    private static void ValidateRequest(CreateRecurringScheduleRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 160)
            throw new ArgumentException("Schedule title must contain 1 to 160 characters.", nameof(request));
        if (request.AmountMinor <= 0)
            throw new ArgumentOutOfRangeException(nameof(request), "Amount must be greater than zero.");
        if (request.CurrencyCode.Length != 3)
            throw new ArgumentException("Currency code must contain exactly three characters.", nameof(request));
        if (request.IntervalCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(request), "Interval count must be greater than zero.");
        if (request.EndDate is not null && request.EndDate < request.StartDate)
            throw new ArgumentException("End date cannot be earlier than start date.", nameof(request));
        _ = RecurrenceCalculator.GetNextOccurrence(
            request.StartDate, request.StartDate, request.IntervalCount, ParseIntervalUnit(request.IntervalUnit));
        _ = ParseKind(request.Kind);
    }

    private static TransactionKind ParseKind(string kind)
    {
        if (Enum.TryParse<TransactionKind>(kind, true, out var parsed) &&
            parsed is TransactionKind.Expense or TransactionKind.Income)
            return parsed;
        throw new ArgumentException("Recurring schedules currently support expense and income transactions.", nameof(kind));
    }

    private static RecurrenceIntervalUnit ParseIntervalUnit(string unit)
    {
        if (Enum.TryParse<RecurrenceIntervalUnit>(unit, true, out var parsed))
            return parsed;
        throw new ArgumentException("Unsupported recurrence interval unit.", nameof(unit));
    }

    private static async Task<(DbAccount Account, DbAccount Category)> ValidateAccountsAsync(
        AppDbContext db,
        string kind,
        Guid accountId,
        Guid? categoryId,
        string currencyCode,
        CancellationToken cancellationToken)
    {
        if (categoryId is null)
            throw new ArgumentException("A category is required for a recurring transaction.", nameof(categoryId));

        var transactionKind = ParseKind(kind);
        var account = await db.Accounts.SingleOrDefaultAsync(
            x => x.Id == accountId && !x.IsArchived, cancellationToken);
        var category = await db.Accounts.SingleOrDefaultAsync(
            x => x.Id == categoryId.Value && !x.IsArchived, cancellationToken);
        if (account is null || category is null)
            throw new ArgumentException("The selected account or category is unavailable.");
        if (account.CurrencyCode != currencyCode || category.CurrencyCode != currencyCode)
            throw new ArgumentException("Account, category, and schedule currencies must match.");

        var validAccount = transactionKind == TransactionKind.Expense
            ? account.Type is AccountType.Asset or AccountType.Liability
            : account.Type == AccountType.Asset;
        var validCategory = category.Kind == AccountKind.Category &&
            category.Type == (transactionKind == TransactionKind.Expense ? AccountType.Expense : AccountType.Income);
        if (!validAccount || !validCategory)
            throw new ArgumentException("The selected accounts do not match the recurring transaction type.");

        return (account, category);
    }
}

public sealed record CreateRecurringScheduleRequest(
    string Title,
    string Kind,
    string CurrencyCode,
    long AmountMinor,
    Guid AccountId,
    Guid? CategoryAccountId,
    DateOnly StartDate,
    DateOnly? EndDate,
    int IntervalCount,
    string IntervalUnit);

public sealed record RecurringScheduleListItem(
    Guid Id,
    string Title,
    string Kind,
    string CurrencyCode,
    long AmountMinor,
    DateOnly StartDate,
    DateOnly NextOccurrenceDate,
    DateOnly? EndDate,
    int IntervalCount,
    string IntervalUnit,
    bool IsPaused);

public sealed record RecurringAccountChoice(Guid Id, string Name, string CurrencyCode, string Type);
public sealed record RecurringCategoryChoice(Guid Id, string Name, string CurrencyCode);
public sealed record RecurringOccurrenceListItem(Guid Id, DateOnly OccurrenceDate, string Status, Guid? TransactionId);
