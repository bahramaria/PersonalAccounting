using Hesabyar.Domain.ValueObjects;

namespace Hesabyar.Domain.Tests;

public sealed class CurrencyPrecisionTests
{
    [Theory]
    [InlineData("IRR", 0, 1)]
    [InlineData("JPY", 0, 1)]
    [InlineData("USD", 2, 100)]
    [InlineData("EUR", 2, 100)]
    [InlineData("KWD", 3, 1_000)]
    [InlineData("CLF", 4, 10_000)]
    public void CurrencyPrecision_UsesCurrencyMinorUnitScale(string currencyCode, int digits, long factor)
    {
        Assert.Equal(digits, CurrencyPrecision.GetMinorUnitDigits(currencyCode));
        Assert.Equal(factor, CurrencyPrecision.GetMinorUnitFactor(currencyCode));
    }

    [Theory]
    [InlineData("")]
    [InlineData("US")]
    [InlineData("USDD")]
    [InlineData("1SD")]
    public void CurrencyPrecision_RejectsInvalidCodes(string currencyCode)
    {
        Assert.Throws<ArgumentException>(() => CurrencyPrecision.GetMinorUnitDigits(currencyCode));
    }
}
