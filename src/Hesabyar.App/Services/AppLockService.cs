using System.Text.Json;
using Hesabyar.Application.Security;
using Microsoft.Maui.Storage;

namespace Hesabyar.App.Services;

public sealed class AppLockService
{
    private const string CredentialKey = "security.pin.credential.v1";
    private const string LockoutKey = "security.pin.lockout.v1";

    public async Task<bool> IsPinEnabledAsync() =>
        !string.IsNullOrWhiteSpace(await SecureStorage.Default.GetAsync(CredentialKey));

    public async Task SetPinAsync(string pin)
    {
        if (!PinHasher.IsValidPin(pin))
            throw new ArgumentException("PIN must contain 4 to 8 digits.", nameof(pin));
        var salt = PinHasher.CreateSalt();
        var hash = await Task.Run(() => PinHasher.Hash(pin, salt));
        var credential = new PinCredential(Convert.ToBase64String(salt), Convert.ToBase64String(hash), PinHasher.DefaultIterations);
        await SecureStorage.Default.SetAsync(CredentialKey, JsonSerializer.Serialize(credential));
        SecureStorage.Default.Remove(LockoutKey);
    }

    public async Task<PinVerificationResult> VerifyPinAsync(string? pin)
    {
        var json = await SecureStorage.Default.GetAsync(CredentialKey);
        if (string.IsNullOrWhiteSpace(json)) return new PinVerificationResult(false, null, false);

        var state = await ReadLockoutAsync();
        if (state.LockoutUntilUtc is { } until && until > DateTimeOffset.UtcNow)
            return new PinVerificationResult(false, until - DateTimeOffset.UtcNow, true);

        if (PinHasher.IsValidPin(pin))
        {
            var credential = JsonSerializer.Deserialize<PinCredential>(json)
                ?? throw new InvalidOperationException("The stored PIN credential is invalid.");
            var salt = Convert.FromBase64String(credential.Salt);
            var expected = Convert.FromBase64String(credential.Hash);
            if (await Task.Run(() => PinHasher.Verify(pin!, salt, expected, credential.Iterations)))
            {
                SecureStorage.Default.Remove(LockoutKey);
                return new PinVerificationResult(true, null, false);
            }
        }

        var attempts = state.FailedAttempts + 1;
        DateTimeOffset? lockoutUntil = attempts < 5 ? null : DateTimeOffset.UtcNow.Add(attempts switch
        {
            5 => TimeSpan.FromSeconds(30),
            6 => TimeSpan.FromMinutes(1),
            7 => TimeSpan.FromMinutes(5),
            _ => TimeSpan.FromMinutes(15)
        });
        await SecureStorage.Default.SetAsync(LockoutKey,
            JsonSerializer.Serialize(new LockoutState(attempts, lockoutUntil)));
        return new PinVerificationResult(false,
            lockoutUntil is null ? null : lockoutUntil - DateTimeOffset.UtcNow, false);
    }

    public async Task<bool> DisablePinAsync(string currentPin)
    {
        if (!(await VerifyPinAsync(currentPin)).IsValid) return false;
        SecureStorage.Default.Remove(CredentialKey);
        SecureStorage.Default.Remove(LockoutKey);
        return true;
    }

    private static async Task<LockoutState> ReadLockoutAsync()
    {
        var json = await SecureStorage.Default.GetAsync(LockoutKey);
        return string.IsNullOrWhiteSpace(json)
            ? new LockoutState(0, null)
            : JsonSerializer.Deserialize<LockoutState>(json) ?? new LockoutState(0, null);
    }

    private sealed record PinCredential(string Salt, string Hash, int Iterations);
    private sealed record LockoutState(int FailedAttempts, DateTimeOffset? LockoutUntilUtc);
}

public readonly record struct PinVerificationResult(bool IsValid, TimeSpan? RetryAfter, bool IsCurrentlyLockedOut);