using Hesabyar.Application.Accounts;
using Hesabyar.Domain.Accounts;
using Hesabyar.Domain.Ledger;
using Hesabyar.Domain.ValueObjects;
using Hesabyar.Application.Transactions;
using Hesabyar.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Hesabyar.Infrastructure.Persistence;

public sealed class EfAccountingRepository(AppDbContext db) : IAccountingRepository
{
    public async Task<AccountSnapshot?> GetAccountAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var account = await db.Accounts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == accountId, cancellationToken);
        return account is null ? null : ToSnapshot(account);
    }

    public async Task<IReadOnlyList<AccountSnapshot>> GetAccountsAsync(bool includeArchived, CancellationToken cancellationToken)
    {
        var query = db.Accounts.AsNoTracking().Where(x => x.Kind != AccountKind.System);
        if (!includeArchived)
            query = query.Where(x => !x.IsArchived);

        var accounts = await query
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return accounts.Select(ToSnapshot).ToArray();
    }

    public async Task<long> GetBalanceMinorAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var total = await db.Postings
            .Where(x => x.AccountId == accountId && !x.Transaction.IsDeleted)
            .Select(x => (long?)x.AmountMinor)
            .SumAsync(cancellationToken);

        return total ?? 0L;
    }

    public async Task<IReadOnlyDictionary<Guid, long>> GetBalancesMinorAsync(
        IReadOnlyCollection<Guid> accountIds,
        CancellationToken cancellationToken)
    {
        if (accountIds.Count == 0)
            return new Dictionary<Guid, long>();

        var balances = await db.Postings
            .Where(x => accountIds.Contains(x.AccountId) && !x.Transaction.IsDeleted)
            .GroupBy(x => x.AccountId)
            .Select(group => new { AccountId = group.Key, Balance = group.Sum(x => x.AmountMinor) })
            .ToDictionaryAsync(x => x.AccountId, x => x.Balance, cancellationToken);

        return balances;
    }

    public Task<bool> HasTransactionsAsync(Guid accountId, CancellationToken cancellationToken)
    {
        return db.Postings.AnyAsync(x => x.AccountId == accountId, cancellationToken);
    }

    public async Task<Guid> GetOrCreateOpeningAccountAsync(CurrencyCode currency, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var existing = await db.Accounts
            .SingleOrDefaultAsync(x => x.SystemKey == "opening" && x.CurrencyCode == currency.Value, cancellationToken);
        if (existing is not null)
            return existing.Id;

        var account = new DbAccount
        {
            Name = "Opening Balance",
            Type = AccountType.Equity,
            Kind = AccountKind.System,
            CurrencyCode = currency.Value,
            SystemKey = "opening",
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            IncludeInTotal = false
        };
        db.Accounts.Add(account);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return account.Id;
        }
        catch (DbUpdateException)
        {
            db.Entry(account).State = EntityState.Detached;
            var concurrent = await db.Accounts
                .SingleAsync(x => x.SystemKey == "opening" && x.CurrencyCode == currency.Value, cancellationToken);
            return concurrent.Id;
        }
    }

    public async Task<Guid> CreateAccountAsync(Account account, LedgerTransaction? openingTransaction, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        db.Accounts.Add(new DbAccount
        {
            Id = account.Id,
            Name = account.Name,
            Type = account.Type,
            Kind = account.Kind,
            ParentId = account.ParentId,
            CurrencyCode = account.Currency.Value,
            Icon = account.Icon,
            Color = account.Color,
            SortOrder = account.SortOrder,
            IsArchived = account.IsArchived,
            IncludeInTotal = account.IncludeInTotal,
            BankName = account.BankName,
            CardLast4 = account.CardLast4,
            AccountNumberMasked = account.AccountNumberMasked,
            CreatedAtUtc = account.CreatedAtUtc,
            UpdatedAtUtc = account.UpdatedAtUtc
        });

        if (openingTransaction is not null)
            db.Transactions.Add(ToEntity(openingTransaction));

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return account.Id;
    }

    public async Task RenameAccountAsync(Guid accountId, string name, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var account = await db.Accounts.SingleAsync(x => x.Id == accountId, cancellationToken);
        account.Name = name;
        account.UpdatedAtUtc = nowUtc;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ArchiveAccountAsync(Guid accountId, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var account = await db.Accounts.SingleAsync(x => x.Id == accountId, cancellationToken);
        account.IsArchived = true;
        account.UpdatedAtUtc = nowUtc;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<Guid> CreateTransactionAsync(LedgerTransaction transaction, CancellationToken cancellationToken)
    {
        db.Transactions.Add(ToEntity(transaction));
        await db.SaveChangesAsync(cancellationToken);
        return transaction.Id;
    }

    public async Task<IReadOnlyList<TransactionSnapshot>> GetTransactionsAsync(
        string? search,
        DateOnly? fromDate,
        DateOnly? toDate,
        Guid? accountId,
        Guid? categoryId,
        long? minimumAmountMinor,
        long? maximumAmountMinor,
        CancellationToken cancellationToken)
    {
        var query = db.Transactions.AsNoTracking().Include(x => x.Postings)
            .Where(x => x.Kind == TransactionKind.Expense || x.Kind == TransactionKind.Income || x.Kind == TransactionKind.Transfer)
            .AsQueryable();
        if (fromDate is not null)
            query = query.Where(x => x.BusinessDate >= fromDate.Value);
        if (toDate is not null)
            query = query.Where(x => x.BusinessDate <= toDate.Value);
        if (accountId is Guid selectedAccountId)
            query = query.Where(x => x.Postings.Any(p => p.AccountId == selectedAccountId));
        if (categoryId is Guid selectedCategoryId)
            query = query.Where(x => x.Postings.Any(p => p.AccountId == selectedCategoryId));
        if (minimumAmountMinor is long minimum)
            query = query.Where(x => x.Postings.Where(p => p.AmountMinor > 0).Sum(p => (long?)p.AmountMinor) >= minimum);
        if (maximumAmountMinor is long maximum)
            query = query.Where(x => x.Postings.Where(p => p.AmountMinor > 0).Sum(p => (long?)p.AmountMinor) <= maximum);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().Replace("ي", "ی", StringComparison.Ordinal)
                .Replace("ك", "ک", StringComparison.Ordinal)
                .Replace("ى", "ی", StringComparison.Ordinal);
            query = query.Where(x => x.Description != null &&
                x.Description.Replace("ي", "ی").Replace("ك", "ک").Replace("ى", "ی").Contains(term));
        }

        var transactions = await query
            .OrderByDescending(x => x.BusinessDate)
            .Take(50_000)
            .ToListAsync(cancellationToken);

        // SQLite cannot translate DateTimeOffset ordering; apply the secondary sort after materialization.
        var orderedTransactions = transactions
            .OrderByDescending(x => x.BusinessDate)
            .ThenByDescending(x => x.CreatedAtUtc);

        return orderedTransactions.Select(ToTransactionSnapshot).ToArray();
    }

    public async Task<TransactionSnapshot?> GetTransactionAsync(Guid transactionId, CancellationToken cancellationToken)
    {
        var transaction = await db.Transactions.AsNoTracking().Include(x => x.Postings)
            .SingleOrDefaultAsync(x => x.Id == transactionId && !x.IsDeleted &&
                (x.Kind == TransactionKind.Expense || x.Kind == TransactionKind.Income || x.Kind == TransactionKind.Transfer),
                cancellationToken);
        return transaction is null ? null : ToTransactionSnapshot(transaction);
    }

    private static TransactionSnapshot ToTransactionSnapshot(DbLedgerTransaction transaction)
    {
        var positive = transaction.Postings.FirstOrDefault(p => p.AmountMinor > 0);
        var negative = transaction.Postings.FirstOrDefault(p => p.AmountMinor < 0);
        var amount = transaction.Postings.Where(p => p.AmountMinor > 0).Sum(p => p.AmountMinor);
        return new TransactionSnapshot(
            transaction.Id,
            transaction.BusinessDate,
            transaction.Kind,
            amount,
            transaction.Kind == TransactionKind.Income ? positive?.AccountId ?? Guid.Empty : negative?.AccountId ?? Guid.Empty,
            transaction.Kind == TransactionKind.Transfer || transaction.Kind == TransactionKind.Expense ? positive?.AccountId : negative?.AccountId,
            transaction.Description,
            transaction.IsConfirmed);
    }

    public async Task<bool> ReplaceTransactionAsync(Guid transactionId, LedgerTransaction replacement, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var existing = await db.Transactions.Include(x => x.Postings)
            .SingleOrDefaultAsync(x => x.Id == transactionId && !x.IsDeleted, cancellationToken);
        if (existing is null)
            return false;

        existing.IsDeleted = true;
        existing.UpdatedAtUtc = nowUtc;
        db.Transactions.Add(ToEntity(replacement));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task SoftDeleteTransactionAsync(Guid transactionId, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var transaction = await db.Transactions.SingleAsync(x => x.Id == transactionId, cancellationToken);
        transaction.IsDeleted = true;
        transaction.UpdatedAtUtc = nowUtc;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static AccountSnapshot ToSnapshot(DbAccount account) =>
        new(
            account.Id,
            account.Name,
            account.Type,
            account.Kind,
            account.CurrencyCode,
            account.ParentId,
            account.IsArchived,
            account.IncludeInTotal,
            account.SortOrder,
            account.BankName,
            account.CardLast4,
            account.AccountNumberMasked);

    private static DbLedgerTransaction ToEntity(LedgerTransaction transaction)
    {
        var entity = new DbLedgerTransaction
        {
            Id = transaction.Id,
            BusinessDate = transaction.BusinessDate,
            Kind = transaction.Kind,
            Description = transaction.Description,
            Source = transaction.Source,
            IsConfirmed = transaction.IsConfirmed,
            CreatedAtUtc = transaction.CreatedAtUtc,
            UpdatedAtUtc = transaction.UpdatedAtUtc
        };

        foreach (var posting in transaction.Postings)
        {
            entity.Postings.Add(new DbPosting
            {
                AccountId = posting.AccountId,
                AmountMinor = posting.Amount.Minor,
                CurrencyCode = posting.Amount.Currency.Value,
                PartyId = posting.PartyId,
                Memo = posting.Memo,
                CreatedAtUtc = transaction.CreatedAtUtc,
                UpdatedAtUtc = transaction.UpdatedAtUtc
            });
        }

        return entity;
    }
}
