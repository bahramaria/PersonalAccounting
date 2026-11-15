using System.Globalization;
using Hesabyar.App.Resources.Strings;
using Hesabyar.Localization;

namespace Hesabyar.App.Services;

public static class MoneyDisplayService
{
    public static string Format(long amountMinor)
    {
        var amount = amountMinor.ToString("N0", CultureInfo.CurrentCulture);
        return $"{BidiTextFormatter.IsolateLeftToRight(amount)} {AppStrings.CurrencyRial}";
    }
}
