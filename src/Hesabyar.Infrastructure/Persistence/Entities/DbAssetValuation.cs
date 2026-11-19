namespace Hesabyar.Infrastructure.Persistence.Entities;

public sealed class DbAssetValuation : DbEntity
{
    public Guid AssetId { get; set; }
    public DateOnly ValuationDate { get; set; }
    public long ValueMinor { get; set; }
    public required string CurrencyCode { get; set; }
    public string? Note { get; set; }
}
