using System.Text.Json;

namespace Hesabyar.Sync.Contracts;

public static class SyncFieldMerge
{
    public static IReadOnlyDictionary<string, StampedSyncFieldValue> Merge(
        IEnumerable<StampedSyncFieldValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var merged = new Dictionary<string, StampedSyncFieldValue>(StringComparer.Ordinal);
        foreach (var candidate in values)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(candidate.Name);

            if (!merged.TryGetValue(candidate.Name, out var current) ||
                current.Stamp.CompareTo(candidate.Stamp) < 0)
            {
                merged[candidate.Name] = candidate with { Value = candidate.Value.Clone() };
            }
        }

        return merged;
    }
}
