using Hesabyar.Application.Transactions;
using Hesabyar.Domain.Accounts;
using Hesabyar.Domain.Ledger;
using Hesabyar.Domain.ValueObjects;

namespace Hesabyar.Application.Accounts;

public interface IAccountingRepository
{
    Task<AccountSnapshot?> GetAccountAsync(Guid accountId, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccountSnapshot>> GetAccountsAsync(bool includeArchived, CancellationToken cancellationToken);
    Task<long> GetBalanceMinorAsync(Guid accountId, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<Guid, long>> GetBalancesMinorAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken);
    Task<bool> HasTransactionsAsync(Guid accountId, CancellationToken cancellationToken);
    Task<Guid> GetOrCreateOpeningAccountAsync(CurrencyCode currency, DateTimeOffset nowUtc, CancellationToken cancellationToken);
    Task<Guid> CreateAccountAsync(Account account, LedgerTransaction? openingTransaction, CancellationToken cancellationToken);
    Task RenameAccountAsync(Guid accountId, string name, DateTimeOffset nowUtc, CancellationToken cancellationToken);
    Task ArchiveAccountAsync(Guid accountId, DateTimeOffset nowUtc, CancellationToken cancellationToken);
    Task<Guid> CreateTransactionAsync(LedgerTransaction transaction, CancellationToken cancellationToken);
    Task<bool> ReplaceTransactionAsync(Guid transactionId, LedgerTransaction replacement, DateTimeOffset nowUtc, CancellationToken cancellationToken);
    Task<IReadOnlyList<TransactionSnapshot>> GetTransactionsAsync(string? search, DateOnly? fromDate, DateOnly? toDate, Guid? accountId, Guid? categoryId, long? minimumAmountMinor, long? maximumAmountMinor, CancellationToken cancellationToken);
    Task SoftDeleteTransactionAsync(Guid transactionId, DateTimeOffset nowUtc, CancellationToken cancellationToken);
    Task<TransactionSnapshot?> GetTransactionAsync(Guid transactionId, CancellationToken cancellationToken);
}
