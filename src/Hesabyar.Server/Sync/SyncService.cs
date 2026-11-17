using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
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

public sealed class SyncService
{
    private readonly IdentityDbContext database;
    private readonly TimeProvider timeProvider;
    private readonly byte[] cursorSigningKey;

    public SyncService(IdentityDbContext database, TimeProvider timeProvider, byte[] tokenSecret)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(tokenSecret);
        if (tokenSecret.Length < 32)
            throw new ArgumentException("The sync cursor signing secret must contain at least 32 bytes.", nameof(tokenSecret));

        this.database = database;
        this.timeProvider = timeProvider;
        cursorSigningKey = HMACSHA256.HashData(
            tokenSecret,
            Encoding.UTF8.GetBytes("Hesabyar.Sync.Cursor.v1"));
    }

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

        var payloads = request.Changes.Select(change => JsonSerializer.Serialize(change, JsonOptions)).ToArray();
        var payloadSizes = payloads.Select(payload => Encoding.UTF8.GetByteCount(payload)).ToArray();
        if (payloadSizes.Any(size => size > MaximumPayloadBytes) || payloadSizes.Sum() > MaximumBatchBytes)
            throw new ArgumentException("The sync payload exceeds the allowed size.");

        await using var transaction = await database.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var deviceIsActive = await database.RefreshTokenSessions.AnyAsync(
            session => session.UserId == userId && session.DeviceId == request.DeviceId &&
                       session.RevokedAtUtc == null && session.ExpiresAtUtc > now,
            cancellationToken);
        if (!deviceIsActive)
            throw new SyncDeviceNotRegisteredException();

        var changeIds = request.Changes.Select(change => change.ChangeId).ToArray();
        var existingRows = await database.SyncChangeLogs
            .Where(change => change.UserId == userId && changeIds.Contains(change.ChangeId))
            .Select(change => new { change.ChangeId, change.Payload })
            .ToListAsync(cancellationToken);
        var existing = existingRows.ToDictionary(row => row.ChangeId, row => row.Payload);
        foreach (var item in request.Changes.Select((change, index) => new { Change = change, Payload = payloads[index] }))
        {
            if (existing.TryGetValue(item.Change.ChangeId, out var storedPayload) &&
                !string.Equals(storedPayload, item.Payload, StringComparison.Ordinal))
                throw new ArgumentException("A previously accepted ChangeId cannot be reused with different content.");
        }
        var newChanges = request.Changes
            .Select((change, index) => new { Change = change, Payload = payloads[index] })
            .Where(item => !existing.ContainsKey(item.Change.ChangeId))
            .ToArray();

        if (newChanges.Length > 0)
        {
            var lastSequence = await database.SyncChangeLogs
                .Where(change => change.UserId == userId && change.DeviceId == request.DeviceId)
                .Select(change => (long?)change.DeviceSequence)
                .MaxAsync(cancellationToken) ?? 0;
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
        return new SyncPushResponse(
            newChanges.Select(item => item.Change.ChangeId).ToArray(),
            request.Changes.Where(change => existing.ContainsKey(change.ChangeId))
                .Select(change => change.ChangeId).ToArray(),
            []);
    }

    public async Task<SyncPullResponse> PullAsync(
        Guid userId, SyncPullRequest request, CancellationToken cancellationToken)
    {
        var errors = SyncContractValidator.Validate(request);
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors));

        var now = timeProvider.GetUtcNow();
        var deviceIsActive = await database.RefreshTokenSessions.AnyAsync(
            session => session.UserId == userId && session.DeviceId == request.DeviceId &&
                       session.RevokedAtUtc == null && session.ExpiresAtUtc > now,
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
        var nextCursor = rows.Count == 0 ? request.Cursor : EncodeCursor(userId, rows[^1].Id);
        return new SyncPullResponse(nextCursor, hasMore, changes);
    }

    private string EncodeCursor(Guid userId, long lastId)
    {
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(new CursorPayload(userId, lastId), JsonOptions);
        var payload = EncodeBase64Url(payloadBytes);
        var signature = HMACSHA256.HashData(cursorSigningKey, Encoding.UTF8.GetBytes(payload));
        return $"{payload}.{EncodeBase64Url(signature)}";
    }

    private long DecodeCursor(string? cursor, Guid userId)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return 0;

        try
        {
            var parts = cursor.Split('.');
            if (parts.Length != 2)
                throw new SyncCursorException();

            var suppliedSignature = DecodeBase64Url(parts[1]);
            var expectedSignature = HMACSHA256.HashData(cursorSigningKey, Encoding.UTF8.GetBytes(parts[0]));
            if (!CryptographicOperations.FixedTimeEquals(suppliedSignature, expectedSignature))
                throw new SyncCursorException();

            var payload = JsonSerializer.Deserialize<CursorPayload>(DecodeBase64Url(parts[0]), JsonOptions);
            if (payload is null || payload.UserId != userId || payload.LastId < 0)
                throw new SyncCursorException();
            return payload.LastId;
        }
        catch (SyncCursorException)
        {
            throw;
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

    private static string EncodeBase64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] DecodeBase64Url(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized = normalized.PadRight(normalized.Length + (4 - normalized.Length % 4) % 4, '=');
        return Convert.FromBase64String(normalized);
    }

    private sealed record CursorPayload(Guid UserId, long LastId);
}
