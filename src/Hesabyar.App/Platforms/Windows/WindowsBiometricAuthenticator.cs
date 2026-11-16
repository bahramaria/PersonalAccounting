using Windows.Security.Credentials.UI;

namespace Hesabyar.App.Services;

public sealed class WindowsBiometricAuthenticator : IBiometricAuthenticator
{
    public async Task<bool> IsAvailableAsync() =>
        await UserConsentVerifier.CheckAvailabilityAsync() == UserConsentVerifierAvailability.Available;

    public async Task<bool> AuthenticateAsync(string title, string reason, string cancelText)
    {
        if (await UserConsentVerifier.CheckAvailabilityAsync() != UserConsentVerifierAvailability.Available)
            return false;

        var result = await UserConsentVerifier.RequestVerificationAsync(reason);
        return result == UserConsentVerificationResult.Verified;
    }
}
