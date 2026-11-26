namespace Hesabyar.Infrastructure.Persistence.Entities;

public sealed class DbRecurringTransactionOccurrence : DbEntity
{
    public Guid RecurringTransactionId { get; set; }
    public DateOnly OccurrenceDate { get; set; }
    public Guid? TransactionId { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset? GeneratedAtUtc { get; set; }
}