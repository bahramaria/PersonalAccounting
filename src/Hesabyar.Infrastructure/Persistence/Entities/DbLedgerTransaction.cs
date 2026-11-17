using Hesabyar.Domain.Ledger;

namespace Hesabyar.Infrastructure.Persistence.Entities;

public sealed class DbLedgerTransaction : DbEntity
{
    public DateOnly BusinessDate { get; set; }
    public TransactionKind Kind { get; set; }
    public string? Description { get; set; }
    public Guid? PartyId { get; set; }
    public Guid? LoanId { get; set; }
    public DateOnly? DueDate { get; set; }
    public TransactionSource Source { get; set; }
    public string? SmsMessageId { get; set; }
    public bool IsConfirmed { get; set; }
    public Guid? FiscalPeriodId { get; set; }
    public string? DocumentNumber { get; set; }
    public long? CounterAmountMinor { get; set; }
    public ICollection<DbPosting> Postings { get; set; } = new List<DbPosting>();
}
