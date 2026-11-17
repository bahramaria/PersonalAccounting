using System.Text.Json;
using Hesabyar.Sync.Contracts;
using Xunit;

namespace Hesabyar.Sync.Contracts.Tests;

public sealed class SyncContractTests
{
    [Fact]
    public void ValidatePush_AcceptsOrderedChangesFromOneDevice()
    {
        var deviceId = Guid.NewGuid();
        var request = new SyncPushRequest(deviceId, [CreateChange(deviceId, 1), CreateChange(deviceId, 2)]);
        Assert.Empty(SyncContractValidator.Validate(request));
    }

    [Fact]
    public void ValidatePush_RejectsMixedDevicesAndNonIncreasingSequences()
    {
        var deviceId = Guid.NewGuid();
        var otherDeviceId = Guid.NewGuid();
        var request = new SyncPushRequest(deviceId, [CreateChange(deviceId, 2), CreateChange(otherDeviceId, 2)]);
        var errors = SyncContractValidator.Validate(request);
        Assert.Contains(errors, error => error.Contains("different device", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("strictly increasing", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateChange_RejectsFieldsOnDelete()
    {
        var change = CreateChange(Guid.NewGuid(), 1) with { Operation = SyncOperation.Delete };
        Assert.Contains(SyncContractValidator.Validate(change),
            error => error.Contains("must not contain field values", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateChange_RejectsDuplicateFieldNames()
    {
        var change = CreateChange(Guid.NewGuid(), 1) with
        {
            Fields = [SyncFieldValue.From("Name", "First"), SyncFieldValue.From("Name", "Second")]
        };
        Assert.Contains(SyncContractValidator.Validate(change),
            error => error.Contains("duplicated", StringComparison.Ordinal));
    }

    [Fact]
    public void Merge_UsesLatestStampIndependentlyForEachField()
    {
        var deviceId = Guid.NewGuid();
        var timestamp = DateTimeOffset.Parse("2026-11-17T10:00:00Z");
        var values = new[]
        {
            new StampedSyncFieldValue("Name", JsonSerializer.SerializeToElement("Old"), new(timestamp, deviceId, 1)),
            new StampedSyncFieldValue("Name", JsonSerializer.SerializeToElement("New"), new(timestamp.AddMinutes(1), deviceId, 2)),
            new StampedSyncFieldValue("Color", JsonSerializer.SerializeToElement("Blue"), new(timestamp, deviceId, 1))
        };
        var result = SyncFieldMerge.Merge(values);
        Assert.Equal("New", result["Name"].Value.GetString());
        Assert.Equal("Blue", result["Color"].Value.GetString());
    }

    [Fact]
    public void Merge_UsesDeviceIdAsDeterministicTimestampTieBreaker()
    {
        var timestamp = DateTimeOffset.Parse("2026-11-17T10:00:00Z");
        var values = new[]
        {
            new StampedSyncFieldValue("Name", JsonSerializer.SerializeToElement("A"),
                new(timestamp, Guid.Parse("00000000-0000-0000-0000-000000000001"), 1)),
            new StampedSyncFieldValue("Name", JsonSerializer.SerializeToElement("B"),
                new(timestamp, Guid.Parse("00000000-0000-0000-0000-000000000002"), 1))
        };
        var result = SyncFieldMerge.Merge(values);
        Assert.Equal("B", result["Name"].Value.GetString());
    }

    [Fact]
    public void ValidatePull_RejectsOutOfRangePageSize()
    {
        var errors = SyncContractValidator.Validate(new SyncPullRequest(null, 501));
        Assert.Single(errors);
    }

    private static SyncChange CreateChange(Guid deviceId, long sequence) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Account", SyncOperation.Upsert, 1,
            new SyncVersionStamp(DateTimeOffset.Parse("2026-11-17T10:00:00Z").AddSeconds(sequence), deviceId, sequence),
            [SyncFieldValue.From("Name", "Cash")]);
}
