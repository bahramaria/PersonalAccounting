namespace Hesabyar.Infrastructure.Persistence.Entities;

public sealed class DbTransactionTag : DbEntity
{
    public Guid TransactionId { get; set; }
    public Guid TagId { get; set; }
}