namespace Hesabyar.Infrastructure.Persistence.Entities;

public sealed class DbRecurringTransaction : DbEntity
{
    public required string Title { get; set; }
    public required string Kind { get; set; }
    public required string CurrencyCode { get; set; }
    public long AmountMinor { get; set; }
    public Guid AccountId { get; set; }
    public Guid? CategoryAccountId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly NextOccurrenceDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public int IntervalCount { get; set; }
    public required string IntervalUnit { get; set; }
    public bool IsPaused { get; set; }
    public DateTimeOffset? LastGeneratedAtUtc { get; set; }
}