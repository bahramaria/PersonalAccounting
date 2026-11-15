namespace Hesabyar.Application.People;

public sealed record CreatePersonRequest(string Name, string? Phone = null, string? Note = null);
public sealed record PersonSnapshot(Guid Id, string Name, string? Phone, string? Note, long BalanceMinor, bool IsArchived);
public sealed record PersonEntrySnapshot(Guid Id, DateOnly Date, string Kind, long AmountMinor, string AccountName, string? Note);
