using Hesabyar.Domain.Accounts;

namespace Hesabyar.Application.Accounts;

public sealed record CreateAccountRequest(
    string Name,
    AccountType Type,
    AccountKind Kind,
    string CurrencyCode = "IRR",
    Guid? ParentId = null,
    long OpeningBalanceMinor = 0,
    DateOnly? OpeningDate = null,
    string? Icon = null,
    string? Color = null,
    int SortOrder = 0,
    string? BankName = null,
    string? CardLast4 = null,
    string? AccountNumberMasked = null);

public sealed record AccountSnapshot(
    Guid Id,
    string Name,
    AccountType Type,
    AccountKind Kind,
    string CurrencyCode,
    Guid? ParentId,
    bool IsArchived,
    bool IncludeInTotal,
    int SortOrder,
    string? BankName,
    string? CardLast4,
    string? AccountNumberMasked);

public sealed record AccountBalance(
    AccountSnapshot Account,
    long BalanceMinor);
