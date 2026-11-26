namespace Hesabyar.Server.Persistence;

public sealed class SyncChangeLogEntity
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public Guid DeviceId { get; set; }
    public Guid ChangeId { get; set; }
    public Guid AggregateId { get; set; }
    public string AggregateType { get; set; } = string.Empty;
    public long DeviceSequence { get; set; }
    public string Payload { get; set; } = string.Empty;
    public DateTimeOffset ReceivedAtUtc { get; set; }
}
