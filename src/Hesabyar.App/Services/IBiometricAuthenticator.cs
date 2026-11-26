namespace Hesabyar.App.Services;

public interface IBiometricAuthenticator
{
    Task<bool> IsAvailableAsync();
    Task<bool> AuthenticateAsync(string title, string reason, string cancelText);
}
