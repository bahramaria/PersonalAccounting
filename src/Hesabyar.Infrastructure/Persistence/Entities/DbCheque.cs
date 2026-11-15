using Hesabyar.Application.Cheques;

namespace Hesabyar.Infrastructure.Persistence.Entities;

public sealed class DbCheque : DbEntity
{
    public ChequeDirection Direction { get; set; }
    public required string BankName { get; set; }
    public required string SerialNumber { get; set; }
    public string? SayadNumber { get; set; }
    public DateOnly DueDate { get; set; }
    public Guid? PersonId { get; set; }
    public long AmountMinor { get; set; }
    public string? Note { get; set; }
    public ChequeStatus Status { get; set; }
    public bool IsArchived { get; set; }
    public Guid? SettlementAccountId { get; set; }
    public Guid? SettlementTransactionId { get; set; }
}
