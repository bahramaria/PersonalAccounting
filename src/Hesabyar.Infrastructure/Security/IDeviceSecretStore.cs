namespace Hesabyar.Infrastructure.Security;

public interface IDeviceSecretStore
{
    Task<string?> GetAsync(string key);
    Task SetAsync(string key, string value);
}
