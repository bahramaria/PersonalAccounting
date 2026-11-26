namespace Hesabyar.Sync.Contracts;

public sealed record SyncChange(
    Guid ChangeId,
    Guid AggregateId,
    string AggregateType,
    SyncOperation Operation,
    int SchemaVersion,
    SyncVersionStamp Stamp,
    IReadOnlyList<SyncFieldValue> Fields);
