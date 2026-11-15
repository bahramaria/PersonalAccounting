using Hesabyar.Application.Loans;

namespace Hesabyar.Infrastructure.Persistence.Entities;

public sealed class DbLoan : DbEntity
{
    public required string Name { get; set; }
    public LoanDirection Direction { get; set; }
    public LoanScheduleType ScheduleType { get; set; }
    public long PrincipalMinor { get; set; }
    public int AnnualRateBasisPoints { get; set; }
    public DateOnly StartDate { get; set; }
    public Guid AccountId { get; set; }
    public Guid LoanAccountId { get; set; }
    public Guid? PersonId { get; set; }
    public string? Note { get; set; }
    public bool IsClosed { get; set; }
    public ICollection<DbLoanInstallment> Installments { get; set; } = new List<DbLoanInstallment>();
}
