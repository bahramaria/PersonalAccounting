using System.Text;
using System.Text.Json;
using Hesabyar.Server.Persistence;
using Hesabyar.Sync.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Hesabyar.Server.Sync;

public sealed class SyncDeviceNotRegisteredException()
    : Exception("The device does not have an active session for this account.");

public sealed class SyncSequenceConflictException()
    : Exception("A device sequence must be greater than every previously accepted sequence.");

public sealed class SyncCursorException()
    : Exception("The sync cursor is invalid or belongs to another account.");

public sealed class SyncService(IdentityDbContext database, TimeProvider timeProvider)
{
    private const int CurrentSchemaVersion = 1;
    private const int MaximumPayloadBytes = 256 * 1024;
    private const int MaximumBatchBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<SyncPushResponse> PushAsync(
        Guid userId, SyncPushRequest request, CancellationToken cancellationToken)
    {
        var errors = SyncContractValidator.Validate(request);
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors));

        if (request.Changes.Any(change => change.SchemaVersion != CurrentSchemaVersion))
            throw new ArgumentException("Unsupported sync schema version.");

        var payloads = request.Changes
            .Select(change => JsonSerializer.Serialize(change, JsonOptions))
            .ToArray();
        var payloadSizes = payloads.Select(payload => Encoding.UTF8.GetByteCount(payload)).ToArray();
        if (payloadSizes.Any(size => size > MaximumPayloadBytes) ||
            payloadSizes.Sum() > MaximumBatchBytes)
            throw new ArgumentException("The sync payload exceeds the allowed size.");

        await using var transaction = await database.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, cancellationToken);

        var now = timeProvider.GetUtcNow();
        var deviceIsActive = await database.RefreshTokenSessions.AnyAsync(
            session => session.UserId == userId &&
                       session.DeviceId == request.DeviceId &&
                       session.RevokedAtUtc == null &&
                       session.ExpiresAtUtc > now,
            cancellationToken);
        if (!deviceIsActive)
            throw new SyncDeviceNotRegisteredException();

        var changeIds = request.Changes.Select(change => change.ChangeId).ToArray();
        var existingIds = await database.SyncChangeLogs
            .Where(change => change.UserId == userId && changeIds.Contains(change.ChangeId))
            .Select(change => change.ChangeId)
            .ToListAsync(cancellationToken);
        var existing = existingIds.ToHashSet();
        var newChanges = request.Changes
            .Select((change, index) => new { Change = change, Payload = payloads[index] })
            .Where(item => !existing.Contains(item.Change.ChangeId))
            .ToArray();

        if (newChanges.Length > 0)
        {
            var lastSequence = await database.SyncChangeLogs
                .Where(change => change.UserId == userId && change.DeviceId == request.DeviceId)
                .Select(change => (long?)change.DeviceSequence)
                .MaxAsync(cancellationToken) ?? 0;

            if (newChanges[0].Change.Stamp.DeviceSequence <= lastSequence)
                throw new SyncSequenceConflictException();

            var previous = lastSequence;
            foreach (var item in newChanges)
            {
                if (item.Change.Stamp.DeviceSequence <= previous)
                    throw new SyncSequenceConflictException();
                previous = item.Change.Stamp.DeviceSequence;
            }

            database.SyncChangeLogs.AddRange(newChanges.Select(item => new SyncChangeLogEntity
            {
                UserId = userId,
                DeviceId = request.DeviceId,
                ChangeId = item.Change.ChangeId,
                AggregateId = item.Change.AggregateId,
                AggregateType = item.Change.AggregateType,
                DeviceSequence = item.Change.Stamp.DeviceSequence,
                Payload = item.Payload,
                ReceivedAtUtc = now
            }));
            await database.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        var duplicateIds = request.Changes
            .Where(change => existing.Contains(change.ChangeId))
            .Select(change => change.ChangeId)
            .ToArray();
        var acceptedIds = newChanges.Select(item => item.Change.ChangeId).ToArray();
        return new SyncPushResponse(acceptedIds, duplicateIds, []);
    }

    public async Task<SyncPullResponse> PullAsync(
        Guid userId, SyncPullRequest request, CancellationToken cancellationToken)
    {
        var errors = SyncContractValidator.Validate(request);
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors));

        var now = timeProvider.GetUtcNow();
        var deviceIsActive = await database.RefreshTokenSessions.AnyAsync(
            session => session.UserId == userId &&
                       session.DeviceId == request.DeviceId &&
                       session.RevokedAtUtc == null &&
                       session.ExpiresAtUtc > now,
            cancellationToken);
        if (!deviceIsActive)
            throw new SyncDeviceNotRegisteredException();

        var cursor = DecodeCursor(request.Cursor, userId);
        var rows = await database.SyncChangeLogs.AsNoTracking()
            .Where(change => change.UserId == userId && change.Id > cursor)
            .OrderBy(change => change.Id)
            .Take(request.PageSize + 1)
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > request.PageSize;
        if (hasMore)
            rows.RemoveAt(rows.Count - 1);

        var changes = rows.Select(row =>
            JsonSerializer.Deserialize<SyncChange>(row.Payload, JsonOptions)
            ?? throw new InvalidOperationException("A stored sync change could not be deserialized."))
            .ToArray();

        var nextCursor = rows.Count == 0
            ? request.Cursor
            : EncodeCursor(userId, rows[^1].Id);
        return new SyncPullResponse(nextCursor, hasMore, changes);
    }

    private static string EncodeCursor(Guid userId, long lastId)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new CursorPayload(userId, lastId), JsonOptions);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static long DecodeCursor(string? cursor, Guid userId)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return 0;

        try
        {
            var normalized = cursor.Replace('-', '+').Replace('_', '/');
            normalized = normalized.PadRight(normalized.Length + (4 - normalized.Length % 4) % 4, '=');
            var bytes = Convert.FromBase64String(normalized);
            var payload = JsonSerializer.Deserialize<CursorPayload>(bytes, JsonOptions);
            if (payload is null || payload.UserId != userId || payload.LastId < 0)
                throw new SyncCursorException();
            return payload.LastId;
        }
        catch (FormatException)
        {
            throw new SyncCursorException();
        }
        catch (JsonException)
        {
            throw new SyncCursorException();
        }
    }

    private sealed record CursorPayload(Guid UserId, long LastId);
}
