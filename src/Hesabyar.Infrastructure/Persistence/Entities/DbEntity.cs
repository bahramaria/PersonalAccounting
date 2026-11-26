namespace Hesabyar.Infrastructure.Persistence.Entities;

public abstract class DbEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public bool IsDeleted { get; set; }
}
