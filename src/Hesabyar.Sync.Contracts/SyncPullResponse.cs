namespace Hesabyar.Sync.Contracts;

public sealed record SyncPullResponse(
    string? NextCursor,
    bool HasMore,
    IReadOnlyList<SyncChange> Changes);
