namespace Hesabyar.Application.Transactions;

public sealed record ExpenseRequest(
    DateOnly BusinessDate,
    long AmountMinor,
    Guid PaymentAccountId,
    Guid ExpenseCategoryId,
    string? Description = null);

public sealed record IncomeRequest(
    DateOnly BusinessDate,
    long AmountMinor,
    Guid DepositAccountId,
    Guid IncomeCategoryId,
    string? Description = null);

public sealed record TransferRequest(
    DateOnly BusinessDate,
    long AmountMinor,
    Guid SourceAccountId,
    Guid DestinationAccountId,
    string? Description = null);

public sealed record CurrencyExchangeRequest(
    DateOnly BusinessDate,
    long SourceAmountMinor,
    long DestinationAmountMinor,
    Guid SourceAccountId,
    Guid DestinationAccountId,
    string? Description = null);
