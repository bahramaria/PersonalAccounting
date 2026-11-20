namespace Hesabyar.Sync.Contracts;

public sealed record SyncPushResponse(
    IReadOnlyList<Guid> AcceptedChangeIds,
    IReadOnlyList<Guid> DuplicateChangeIds,
    IReadOnlyList<SyncRejectedChange> RejectedChanges);

public sealed record SyncRejectedChange(
    Guid ChangeId,
    string Code,
    string Message);
