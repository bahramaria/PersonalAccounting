using Hesabyar.Infrastructure.Security;

namespace Hesabyar.App.Services;

public sealed record CloudSession(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAtUtc,
    Guid UserId,
    string PhoneNumber,
    Guid DeviceId);

public sealed class CloudSessionStore(IDeviceSecretStore secretStore)
{
    private const string AccessTokenKey = "cloud.session.access-token";
    private const string AccessTokenExpiryKey = "cloud.session.access-token-expiry";
    private const string RefreshTokenKey = "cloud.session.refresh-token";
    private const string RefreshTokenExpiryKey = "cloud.session.refresh-token-expiry";
    private const string UserIdKey = "cloud.session.user-id";
    private const string PhoneNumberKey = "cloud.session.phone-number";
    private const string DeviceIdKey = "cloud.device-id";

    public async Task<Guid> GetOrCreateDeviceIdAsync()
    {
        var stored = await secretStore.GetAsync(DeviceIdKey);
        if (Guid.TryParse(stored, out var deviceId) && deviceId != Guid.Empty)
            return deviceId;

        deviceId = Guid.NewGuid();
        await secretStore.SetAsync(DeviceIdKey, deviceId.ToString("D"));
        return deviceId;
    }

    public async Task SaveAsync(
        string accessToken,
        DateTimeOffset accessTokenExpiresAtUtc,
        string refreshToken,
        DateTimeOffset refreshTokenExpiresAtUtc,
        Guid userId,
        string phoneNumber,
        Guid deviceId)
    {
        if (string.IsNullOrWhiteSpace(accessToken) ||
            string.IsNullOrWhiteSpace(refreshToken) ||
            userId == Guid.Empty || deviceId == Guid.Empty ||
            string.IsNullOrWhiteSpace(phoneNumber))
            throw new ArgumentException("The cloud session is incomplete.");

        await secretStore.SetAsync(AccessTokenKey, accessToken);
        await secretStore.SetAsync(AccessTokenExpiryKey, accessTokenExpiresAtUtc.ToString("O"));
        await secretStore.SetAsync(RefreshTokenKey, refreshToken);
        await secretStore.SetAsync(RefreshTokenExpiryKey, refreshTokenExpiresAtUtc.ToString("O"));
        await secretStore.SetAsync(UserIdKey, userId.ToString("D"));
        await secretStore.SetAsync(PhoneNumberKey, phoneNumber);
        await secretStore.SetAsync(DeviceIdKey, deviceId.ToString("D"));
    }

    public async Task<CloudSession?> GetAsync()
    {
        var accessToken = await secretStore.GetAsync(AccessTokenKey);
        var refreshToken = await secretStore.GetAsync(RefreshTokenKey);
        var accessExpiry = await secretStore.GetAsync(AccessTokenExpiryKey);
        var refreshExpiry = await secretStore.GetAsync(RefreshTokenExpiryKey);
        var userId = await secretStore.GetAsync(UserIdKey);
        var phone = await secretStore.GetAsync(PhoneNumberKey);
        var deviceId = await secretStore.GetAsync(DeviceIdKey);

        if (string.IsNullOrWhiteSpace(accessToken) ||
            string.IsNullOrWhiteSpace(refreshToken) ||
            !DateTimeOffset.TryParse(accessExpiry, out var accessExpiresAtUtc) ||
            !DateTimeOffset.TryParse(refreshExpiry, out var refreshExpiresAtUtc) ||
            !Guid.TryParse(userId, out var parsedUserId) ||
            !Guid.TryParse(deviceId, out var parsedDeviceId) ||
            parsedUserId == Guid.Empty || parsedDeviceId == Guid.Empty ||
            string.IsNullOrWhiteSpace(phone))
            return null;

        return new CloudSession(accessToken, accessExpiresAtUtc, refreshToken,
            refreshExpiresAtUtc, parsedUserId, phone, parsedDeviceId);
    }

    public async Task ClearAsync()
    {
        foreach (var key in new[]
        {
            AccessTokenKey, AccessTokenExpiryKey, RefreshTokenKey,
            RefreshTokenExpiryKey, UserIdKey, PhoneNumberKey
        })
        {
            try
            {
                await secretStore.SetAsync(key, string.Empty);
            }
            catch (Exception)
            {
                // Keep clearing the remaining credentials if a platform secure store fails.
            }
        }
    }
}
