namespace Hesabyar.Sync.Contracts;

public sealed record SyncPullRequest(string? Cursor, int PageSize = 200, Guid DeviceId = default);
