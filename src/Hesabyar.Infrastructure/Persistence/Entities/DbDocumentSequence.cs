namespace Hesabyar.Infrastructure.Persistence.Entities;

public sealed class DbDocumentSequence : DbEntity
{
    public Guid FiscalPeriodId { get; set; }
    public int LastNumber { get; set; }
}