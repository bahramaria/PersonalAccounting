namespace Hesabyar.Sync.Contracts;

public readonly record struct SyncVersionStamp(
    DateTimeOffset ChangedAtUtc,
    Guid DeviceId,
    long DeviceSequence) : IComparable<SyncVersionStamp>
{
    public int CompareTo(SyncVersionStamp other)
    {
        var timestampComparison = ChangedAtUtc.CompareTo(other.ChangedAtUtc);
        if (timestampComparison != 0)
            return timestampComparison;

        var deviceComparison = DeviceId.CompareTo(other.DeviceId);
        return deviceComparison != 0
            ? deviceComparison
            : DeviceSequence.CompareTo(other.DeviceSequence);
    }
}
