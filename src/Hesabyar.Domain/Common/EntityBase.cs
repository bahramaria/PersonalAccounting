namespace Hesabyar.Domain.Common;
public abstract class EntityBase
{
    protected EntityBase(DateTimeOffset nowUtc) { Id = Guid.CreateVersion7(); CreatedAtUtc = nowUtc.ToUniversalTime(); UpdatedAtUtc = CreatedAtUtc; }
    public Guid Id { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public bool IsDeleted { get; private set; }
    public void MarkUpdated(DateTimeOffset nowUtc) => UpdatedAtUtc = nowUtc.ToUniversalTime();
    public void SoftDelete(DateTimeOffset nowUtc) { IsDeleted = true; MarkUpdated(nowUtc); }
}