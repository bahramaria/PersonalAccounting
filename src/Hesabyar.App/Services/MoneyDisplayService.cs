using System.Globalization;
using System.Text;
using Hesabyar.App.Resources.Strings;
using Hesabyar.Domain.ValueObjects;
using Hesabyar.Localization;

namespace Hesabyar.App.Services;

public static class MoneyDisplayService
{
    public static string Format(long amountMinor, string currencyCode = "IRR")
    {
        var amount = FormatNumber(amountMinor, currencyCode);
        var unit = string.Equals(currencyCode, "IRR", StringComparison.OrdinalIgnoreCase)
            ? AppStrings.CurrencyRial
            : currencyCode.ToUpperInvariant();
        return $"{BidiTextFormatter.IsolateLeftToRight(amount)} {unit}";
    }

    public static string FormatNumber(long amountMinor, string currencyCode)
    {
        var digits = CurrencyPrecision.GetMinorUnitDigits(currencyCode);
        var majorAmount = amountMinor / (decimal)CurrencyPrecision.GetMinorUnitFactor(currencyCode);
        return majorAmount.ToString($"N{digits}", CultureInfo.CurrentCulture);
    }

    public static string FormatInput(long amountMinor, string currencyCode)
    {
        var digits = CurrencyPrecision.GetMinorUnitDigits(currencyCode);
        var majorAmount = amountMinor / (decimal)CurrencyPrecision.GetMinorUnitFactor(currencyCode);
        return majorAmount.ToString($"F{digits}", CultureInfo.InvariantCulture);
    }

    public static bool TryParse(string? value, string currencyCode, out long amountMinor, bool allowZero = false)
    {
        amountMinor = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var normalized = new StringBuilder(value.Length);
        foreach (var character in value.Trim())
        {
            normalized.Append(character switch
            {
                >= '۰' and <= '۹' => (char)('0' + character - '۰'),
                >= '٠' and <= '٩' => (char)('0' + character - '٠'),
                '٫' => '.',
                '٬' or ',' or ' ' or '\u00A0' or '\u202F' => '\0',
                _ => character
            });
        }
        var text = normalized.ToString().Replace("\0", string.Empty, StringComparison.Ordinal);
        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var major) ||
            major < 0 || (!allowZero && major == 0))
            return false;
        try
        {
            var scaled = major * CurrencyPrecision.GetMinorUnitFactor(currencyCode);
            if (scaled != decimal.Truncate(scaled) || scaled > long.MaxValue) return false;
            amountMinor = decimal.ToInt64(scaled);
            return allowZero || amountMinor > 0;
        }
        catch (OverflowException) { return false; }
        catch (ArgumentException) { return false; }
    }
}
