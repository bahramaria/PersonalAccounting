namespace Hesabyar.Infrastructure.Persistence.Entities;

public sealed class DbPosting : DbEntity
{
    public Guid TransactionId { get; set; }
    public Guid AccountId { get; set; }
    public long AmountMinor { get; set; }
    public required string CurrencyCode { get; set; }
    public Guid? PartyId { get; set; }
    public string? Memo { get; set; }
    public DbLedgerTransaction Transaction { get; set; } = null!;
    public DbAccount Account { get; set; } = null!;
}
