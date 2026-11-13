using Hesabyar.Localization.Calendar;
using Hesabyar.Localization.Numbers;
using Hesabyar.Localization;
namespace Hesabyar.Localization.Tests;
public sealed class LocalizationTests
{
    [Theory]
    [InlineData("۱۴۰۵/۰۸/۲۲", "1405/08/22")]
    [InlineData("١٢٣٤٥٦", "123456")]
    public void ToLatinDigits_NormalizesPersianAndArabicDigits(string input, string expected) => Assert.Equal(expected, new NumberNormalizer().ToLatinDigits(input));
    [Theory]
    [InlineData("۱۲۳٬۴۵۶ تومان", 123456)]
    [InlineData("1,234,567", 1234567)]
    [InlineData("۱۲۳۴٫۵", 1234.5)]
    public void TryParseAmount_ParsesSupportedFormats(string input, decimal expected)
    {
        Assert.True(new NumberNormalizer().TryParseAmount(input, out var actual));
        Assert.Equal(expected, actual);
    }
    [Fact]
    public void IsolateLeftToRight_WrapsNumericTextWithUnicodeIsolates()
    {
        var result = BidiTextFormatter.IsolateLeftToRight("-۱۲۳٬۴۵۶");

        Assert.Equal("\u2066-۱۲۳٬۴۵۶\u2069", result);
    }

    [Fact]
    public void IsolateLeftToRight_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => BidiTextFormatter.IsolateLeftToRight(null!));
    }

    [Fact]
    public void PersianDate_RoundTrips()
    {
        var calendar = new PersianCalendarService();
        var source = new DateOnly(2026, 11, 13);
        Assert.Equal(source, calendar.ToGregorian(calendar.ToPersian(source)));
    }
    [Fact]
    public void AddMonths_ClampsDayAtMonthEnd()
    {
        var result = new PersianCalendarService().AddMonths(new(1405, 6, 31), 1);
        Assert.Equal(new PersianDate(1405, 7, 30), result);
    }
}