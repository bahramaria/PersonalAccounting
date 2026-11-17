using Hesabyar.Domain.Ledger;

namespace Hesabyar.Application.Transactions;

public sealed record TransactionSnapshot(
    Guid Id,
    DateOnly BusinessDate,
    TransactionKind Kind,
    long AmountMinor,
    Guid AccountId,
    Guid? CounterpartyAccountId,
    string? Description,
    bool IsConfirmed,
    string? DocumentNumber = null,
    long? CounterAmountMinor = null,
    string? CurrencyCode = null,
    string? CounterCurrencyCode = null);
