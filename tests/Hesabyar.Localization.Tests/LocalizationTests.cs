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
    [Theory]
    [InlineData(CalendarKind.Persian, "۱۴۰۵/۰۸/۲۲", 2026, 11, 13)]
    [InlineData(CalendarKind.Gregorian, "2026/11/13", 2026, 11, 13)]
    [InlineData(CalendarKind.Gregorian, "٢٠٢٤/٠٢/٢٩", 2024, 2, 29)]
    public void DateCalendarService_ParsesSelectedCalendar(CalendarKind kind, string input, int year, int month, int day)
    {
        var service = new DateCalendarService(new PersianCalendarService()) { CurrentCalendar = kind };
        Assert.True(service.TryParse(input, out var actual));
        Assert.Equal(new DateOnly(year, month, day), actual);
        Assert.Equal(kind == CalendarKind.Persian ? "1405/08/22" : $"{year:0000}/{month:00}/{day:00}", service.Format(actual));
    }

    [Theory]
    [InlineData(CalendarKind.Persian, "۱۴۰۴/۱۲/۳۰")]
    [InlineData(CalendarKind.Gregorian, "2025/02/29")]
    [InlineData(CalendarKind.Gregorian, "2026/13/01")]
    public void DateCalendarService_RejectsInvalidDates(CalendarKind kind, string input)
    {
        var service = new DateCalendarService(new PersianCalendarService()) { CurrentCalendar = kind };
        Assert.False(service.TryParse(input, out _));
    }
}
