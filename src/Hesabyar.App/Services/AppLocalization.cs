using System.Globalization;
using Hesabyar.App.Resources.Strings;
using Microsoft.Maui.Storage;

namespace Hesabyar.App.Services;

public static class AppLocalization
{
    public static void Initialize()
    {
        var language = Preferences.Get("app.language", "fa-IR");
        var culture = CultureInfo.GetCultureInfo(language == "en-US" ? "en-US" : "fa-IR");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        AppStrings.Culture = culture;
    }
}
