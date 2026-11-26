using Microsoft.Maui.Storage;

namespace Hesabyar.App.Services;

public static class CloudApiConfiguration
{
    private const string BaseAddressKey = "cloud.api-base-address";

    public static Uri? GetBaseAddress()
    {
        var value = Preferences.Default.Get(BaseAddressKey, string.Empty);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var address) ||
            address.Scheme != Uri.UriSchemeHttps)
            return null;

        return address;
    }

    public static void SetBaseAddress(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var address) ||
            address.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(address.UserInfo))
            throw new ArgumentException("The cloud API address must be an absolute HTTPS URL.", nameof(value));

        Preferences.Default.Set(BaseAddressKey, address.ToString().TrimEnd('/'));
    }
}
