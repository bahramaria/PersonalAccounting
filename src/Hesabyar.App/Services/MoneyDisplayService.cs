using Hesabyar.App.Resources.Strings;
using Hesabyar.Localization;
using Microsoft.Maui.Storage;

namespace Hesabyar.App.Services;

public static class MoneyDisplayService
{
    public static string Format(long amountMinor, bool includeCurrency = true) =>
        Format((decimal)amountMinor, includeCurrency);

    public static string Format(decimal amountMinor, bool includeCurrency = true)
    {
        if (Preferences.Get("display.hide-amounts", false))
            return AppStrings.AmountHidden;

        var formatted = amountMinor.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);
        var isolated = BidiTextFormatter.IsolateLeftToRight(formatted);
        return includeCurrency ? $"{isolated} {AppStrings.CurrencyRial}" : isolated;
    }
}
