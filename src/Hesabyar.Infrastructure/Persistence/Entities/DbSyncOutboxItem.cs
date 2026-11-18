namespace Hesabyar.Infrastructure.Persistence.Entities;

public sealed class DbSyncOutboxItem : DbEntity
{
    public Guid AggregateId { get; set; }
    public required string AggregateType { get; set; }
    public int Operation { get; set; }
    public int SchemaVersion { get; set; }
    public DateTimeOffset ChangedAtUtc { get; set; }
    public long DeviceSequence { get; set; }
    public required string FieldsJson { get; set; }
    public DateTimeOffset? AcknowledgedAtUtc { get; set; }
}
