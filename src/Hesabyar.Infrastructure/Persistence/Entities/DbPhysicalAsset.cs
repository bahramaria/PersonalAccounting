namespace Hesabyar.Infrastructure.Persistence.Entities;

public enum PhysicalAssetType
{
    Gold = 1,
    ForeignCurrency = 2,
    Vehicle = 3,
    Property = 4,
    Equipment = 5,
    Other = 6
}

public sealed class DbPhysicalAsset : DbEntity
{
    public required string Name { get; set; }
    public PhysicalAssetType Type { get; set; }
    public decimal Quantity { get; set; } = 1m;
    public string? Unit { get; set; }
    public required string CurrencyCode { get; set; }
    public DateOnly? AcquiredOn { get; set; }
    public long AcquisitionCostMinor { get; set; }
    public bool IsArchived { get; set; }
    public string? Note { get; set; }
    public ICollection<DbAssetValuation> Valuations { get; set; } = new List<DbAssetValuation>();
}
