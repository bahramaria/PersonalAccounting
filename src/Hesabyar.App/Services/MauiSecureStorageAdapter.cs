using Hesabyar.Infrastructure.Security;
using Microsoft.Maui.Storage;

namespace Hesabyar.App.Services;

public sealed class MauiSecureStorageAdapter : IDeviceSecretStore
{
    public Task<string?> GetAsync(string key) => SecureStorage.Default.GetAsync(key);

    public Task SetAsync(string key, string value) => SecureStorage.Default.SetAsync(key, value);
}
