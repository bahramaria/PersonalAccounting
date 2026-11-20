namespace Hesabyar.Infrastructure.Persistence.Entities;

public sealed class DbLoanInstallment : DbEntity
{
    public Guid LoanId { get; set; }
    public int Sequence { get; set; }
    public DateOnly DueDate { get; set; }
    public long PrincipalDueMinor { get; set; }
    public long InterestDueMinor { get; set; }
    public long PaidPrincipalMinor { get; set; }
    public long PaidInterestMinor { get; set; }
}
