using Hesabyar.Domain.Accounts;
using Hesabyar.Domain.Ledger;
using Hesabyar.Domain.ValueObjects;

namespace Hesabyar.Application.Accounts;

public interface IAccountingRepository
{
    Task<AccountSnapshot?> GetAccountAsync(Guid accountId, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccountSnapshot>> GetAccountsAsync(bool includeArchived, CancellationToken cancellationToken);
    Task<long> GetBalanceMinorAsync(Guid accountId, CancellationToken cancellationToken);
    Task<bool> HasTransactionsAsync(Guid accountId, CancellationToken cancellationToken);
    Task<Guid> GetOrCreateOpeningAccountAsync(CurrencyCode currency, DateTimeOffset nowUtc, CancellationToken cancellationToken);
    Task<Guid> CreateAccountAsync(Account account, LedgerTransaction? openingTransaction, CancellationToken cancellationToken);
    Task RenameAccountAsync(Guid accountId, string name, DateTimeOffset nowUtc, CancellationToken cancellationToken);
    Task ArchiveAccountAsync(Guid accountId, DateTimeOffset nowUtc, CancellationToken cancellationToken);
    Task<Guid> CreateTransactionAsync(LedgerTransaction transaction, CancellationToken cancellationToken);
}
