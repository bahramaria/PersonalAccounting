using System.Text.Json;

namespace Hesabyar.Sync.Contracts;

public sealed record StampedSyncFieldValue(
    string Name,
    JsonElement Value,
    SyncVersionStamp Stamp);
