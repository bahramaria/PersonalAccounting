using System.Globalization;
using Hesabyar.App.Resources.Strings;
using Hesabyar.Localization;

namespace Hesabyar.App.Services;

public static class MoneyDisplayService
{
    public static string Format(long amountMinor, string currencyCode = "IRR")
    {
        var amount = amountMinor.ToString("N0", CultureInfo.CurrentCulture);
        var unit = string.Equals(currencyCode, "IRR", StringComparison.OrdinalIgnoreCase)
            ? AppStrings.CurrencyRial
            : currencyCode.ToUpperInvariant();
        return $"{BidiTextFormatter.IsolateLeftToRight(amount)} {unit}";
    }
}
