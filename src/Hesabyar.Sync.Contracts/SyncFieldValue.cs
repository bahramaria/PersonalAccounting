using System.Text.Json;

namespace Hesabyar.Sync.Contracts;

public sealed record SyncFieldValue(string Name, JsonElement Value)
{
    public static SyncFieldValue From<T>(string name, T value) =>
        new(name, JsonSerializer.SerializeToElement(value));
}
