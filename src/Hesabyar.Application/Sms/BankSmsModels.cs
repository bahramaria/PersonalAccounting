namespace Hesabyar.Application.Sms;

public enum BankSmsTransactionType
{
    Unknown = 0,
    Expense = 1,
    Income = 2
}

public enum BankSmsParseStatus
{
    Parsed = 0,
    NeedsReview = 1,
    Unsupported = 2
}

public sealed record BankSmsRule(
    string BankKey,
    IReadOnlyCollection<string> SenderAliases,
    IReadOnlyCollection<string> ExpenseKeywords,
    IReadOnlyCollection<string> IncomeKeywords);

public sealed record ParsedBankSms(
    BankSmsParseStatus Status,
    string? BankKey,
    BankSmsTransactionType TransactionType,
    long? AmountMinor,
    long? BalanceMinor,
    string? CardLastFour,
    DateOnly? BusinessDate,
    TimeOnly? TransactionTime,
    string DedupeHash,
    string NormalizedSender,
    string NormalizedBody);
