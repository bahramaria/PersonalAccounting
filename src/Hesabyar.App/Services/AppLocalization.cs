using System.Globalization;
using Hesabyar.App.Resources.Strings;

namespace Hesabyar.App.Services;

public static class AppLocalization
{
    public static void Initialize()
    {
        var culture = CultureInfo.GetCultureInfo("fa-IR");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        AppStrings.Culture = culture;
    }
}
