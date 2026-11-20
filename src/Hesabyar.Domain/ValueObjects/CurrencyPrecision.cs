namespace Hesabyar.Domain.ValueObjects;

public static class CurrencyPrecision
{
    private static readonly HashSet<string> ZeroDecimalCurrencies = new(StringComparer.Ordinal)
    {
        "BIF", "CLP", "DJF", "GNF", "IRR", "ISK", "JPY", "KMF", "KRW",
        "PYG", "RWF", "UGX", "UYI", "VND", "VUV", "XAF", "XOF", "XPF"
    };

    private static readonly HashSet<string> ThreeDecimalCurrencies = new(StringComparer.Ordinal)
    {
        "BHD", "IQD", "JOD", "KWD", "LYD", "OMR", "TND"
    };

    public static int GetMinorUnitDigits(string currencyCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currencyCode);
        var code = currencyCode.Trim().ToUpperInvariant();
        if (code.Length != 3 || code.Any(c => !char.IsAsciiLetter(c)))
            throw new ArgumentException("Currency code must contain three ASCII letters.", nameof(currencyCode));
        if (ZeroDecimalCurrencies.Contains(code)) return 0;
        if (ThreeDecimalCurrencies.Contains(code)) return 3;
        if (code == "CLF") return 4;
        return 2;
    }

    public static long GetMinorUnitFactor(string currencyCode) =>
        GetMinorUnitDigits(currencyCode) switch
        {
            0 => 1L,
            2 => 100L,
            3 => 1_000L,
            4 => 10_000L,
            var digits => checked((long)Math.Pow(10, digits))
        };
}
