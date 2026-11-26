namespace Hesabyar.Sync.Contracts;

public sealed record SyncPushRequest(
    Guid DeviceId,
    IReadOnlyList<SyncChange> Changes);
