namespace Hesabyar.Application.People;

public sealed record CreatePersonRequest(string Name, string? Phone = null, string? Note = null);
public sealed record PersonSnapshot(Guid Id, string Name, string? Phone, string? Note, long BalanceMinor, bool IsArchived, string CurrencyCode = "IRR");
public sealed record PersonEntrySnapshot(Guid Id, DateOnly Date, string Kind, long AmountMinor, string AccountName, string? Note, DateOnly? DueDate, long BalanceEffectMinor, DateTimeOffset CreatedAtUtc, string CurrencyCode = "IRR");
public sealed record PersonDueSnapshot(DateOnly DueDate, long RemainingAmountMinor, bool TheyOweMe);
