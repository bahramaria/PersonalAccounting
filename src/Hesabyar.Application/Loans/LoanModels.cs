namespace Hesabyar.Application.Loans;

public enum LoanDirection { Given, Received }
public enum LoanScheduleType { Annuity, Manual }

public sealed record ManualInstallmentRequest(DateOnly DueDate, long PrincipalMinor, long InterestMinor);
public sealed record CreateLoanRequest(
    string Name, LoanDirection Direction, Guid AccountId, Guid? PersonId, long PrincipalMinor,
    DateOnly StartDate, LoanScheduleType ScheduleType, int InstallmentCount,
    int AnnualRateBasisPoints, IReadOnlyList<ManualInstallmentRequest>? ManualSchedule, string? Note);

public sealed record LoanInstallmentSnapshot(
    Guid Id, int Sequence, DateOnly DueDate, long PrincipalDueMinor, long InterestDueMinor,
    long PaidPrincipalMinor, long PaidInterestMinor)
{
    public long RemainingPrincipalMinor => PrincipalDueMinor - PaidPrincipalMinor;
    public long RemainingInterestMinor => InterestDueMinor - PaidInterestMinor;
    public long RemainingMinor => RemainingPrincipalMinor + RemainingInterestMinor;
}

public sealed record LoanSnapshot(
    Guid Id, string Name, LoanDirection Direction, LoanScheduleType ScheduleType,
    long PrincipalMinor, int AnnualRateBasisPoints, DateOnly StartDate, string AccountName,
    string? PersonName, string? Note, bool IsClosed, IReadOnlyList<LoanInstallmentSnapshot> Installments)
{
    public long RemainingPrincipalMinor => Installments.Sum(x => x.RemainingPrincipalMinor);
    public long RemainingInterestMinor => Installments.Sum(x => x.RemainingInterestMinor);
    public long RemainingMinor => checked(RemainingPrincipalMinor + RemainingInterestMinor);
    public LoanInstallmentSnapshot? NextInstallment => Installments
        .Where(x => x.RemainingMinor > 0).OrderBy(x => x.DueDate).ThenBy(x => x.Sequence).FirstOrDefault();
}

public sealed record LoanOverviewSnapshot(
    LoanSnapshot Loan, DateOnly? NextDueDate, long NextInstallmentRemainingMinor, int OverdueDays);
