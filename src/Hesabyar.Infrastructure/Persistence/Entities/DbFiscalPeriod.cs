namespace Hesabyar.Infrastructure.Persistence.Entities;

public sealed class DbFiscalPeriod : DbEntity
{
    public required string Name { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsClosed { get; set; }
}