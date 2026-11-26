using System.Text.Json;
using Hesabyar.Infrastructure.Persistence;
using Hesabyar.Sync.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Hesabyar.App.Services;

public sealed record SyncOutboxPushResult(
    int SentCount,
    int AcknowledgedCount,
    IReadOnlyList<SyncRejectedChange> RejectedChanges);

public sealed class SyncOutboxPushService(
    IDbContextFactory<AppDbContext> dbContextFactory,
    CloudSessionStore sessionStore,
    SyncApiClient syncApiClient,
    TimeProvider timeProvider)
{
    public const int MaximumBatchSize = 100;

    public async Task<SyncOutboxPushResult> PushPendingAsync(
        CancellationToken cancellationToken = default)
    {
        var session = await sessionStore.GetAsync();
        if (session is null)
            throw new InvalidOperationException("Sign in to the cloud account before synchronizing.");

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var pending = await db.SyncOutbox
            .Where(x => x.AcknowledgedAtUtc == null)
            .OrderBy(x => x.DeviceSequence)
            .Take(MaximumBatchSize)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
            return new SyncOutboxPushResult(0, 0, []);

        var changes = pending.Select(item =>
        {
            var fields = JsonSerializer.Deserialize<List<SyncFieldValue>>(item.FieldsJson)
                ?? throw new InvalidOperationException($"Outbox item '{item.Id}' contains invalid field data.");

            return new SyncChange(
                item.Id,
                item.AggregateId,
                item.AggregateType,
                (SyncOperation)item.Operation,
                item.SchemaVersion,
                new SyncVersionStamp(item.ChangedAtUtc.ToUniversalTime(), session.DeviceId, item.DeviceSequence),
                fields);
        }).ToArray();

        var response = await syncApiClient.PushAsync(changes, cancellationToken);
        var acknowledgedIds = response.AcceptedChangeIds
            .Concat(response.DuplicateChangeIds)
            .ToHashSet();

        var acknowledgedAtUtc = timeProvider.GetUtcNow();
        var acknowledgedCount = 0;
        foreach (var item in pending)
        {
            if (!acknowledgedIds.Contains(item.Id))
                continue;

            item.AcknowledgedAtUtc = acknowledgedAtUtc;
            acknowledgedCount++;
        }

        if (acknowledgedCount > 0)
            await db.SaveChangesAsync(cancellationToken);

        return new SyncOutboxPushResult(
            pending.Count,
            acknowledgedCount,
            response.RejectedChanges);
    }
}
