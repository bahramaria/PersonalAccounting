namespace Hesabyar.Infrastructure.Persistence.Entities;

public sealed class DbBudget : DbEntity
{
    public Guid CategoryAccountId { get; set; }
    public DbAccount CategoryAccount { get; set; } = null!;
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public long LimitMinor { get; set; }
    public required string CurrencyCode { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Note { get; set; }
}