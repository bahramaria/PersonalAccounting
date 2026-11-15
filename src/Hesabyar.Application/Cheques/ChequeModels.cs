namespace Hesabyar.Application.Cheques;

public enum ChequeDirection { Received, Issued }
public enum ChequeStatus { Pending, Cleared, Returned, Spent, Voided }

public sealed record CreateChequeRequest(
    ChequeDirection Direction, string BankName, string SerialNumber, string? SayadNumber,
    DateOnly DueDate, Guid? PersonId, long AmountMinor, string? Note);

public sealed record UpdateChequeRequest(
    string BankName, string SerialNumber, string? SayadNumber, DateOnly DueDate,
    Guid? PersonId, long AmountMinor, string? Note);

public sealed record ChequeSnapshot(
    Guid Id, ChequeDirection Direction, string BankName, string SerialNumber, string? SayadNumber,
    DateOnly DueDate, Guid? PersonId, string? PersonName, long AmountMinor, string? Note,
    ChequeStatus Status, bool IsArchived, Guid? SettlementAccountId);

public sealed record ChequeListSnapshot(
    IReadOnlyList<ChequeSnapshot> Cheques,
    int DueWithinSevenDays,
    int DueWithinThirtyDays);
