using System.Globalization;
namespace Hesabyar.Localization.Calendar;
public sealed class PersianCalendarService : ICalendarService
{
    private readonly PersianCalendar _calendar = new();
    public PersianDate ToPersian(DateOnly gregorian)
    {
        var value = gregorian.ToDateTime(TimeOnly.MinValue);
        return new(_calendar.GetYear(value), _calendar.GetMonth(value), _calendar.GetDayOfMonth(value));
    }
    public DateOnly ToGregorian(PersianDate persian)
    {
        Validate(persian);
        return DateOnly.FromDateTime(_calendar.ToDateTime(persian.Year, persian.Month, persian.Day, 0, 0, 0, 0));
    }
    public bool IsLeapYear(int persianYear) => _calendar.IsLeapYear(persianYear);
    public int DaysInMonth(int persianYear, int month)
    {
        if (month is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(month));
        return _calendar.GetDaysInMonth(persianYear, month);
    }
    public PersianDate AddMonths(PersianDate date, int months, MonthEndPolicy policy = MonthEndPolicy.Clamp)
    {
        Validate(date);
        var absoluteMonth = checked((date.Year * 12) + date.Month - 1 + months);
        var year = Math.DivRem(absoluteMonth, 12, out var monthIndex);
        if (monthIndex < 0) { year--; monthIndex += 12; }
        var month = monthIndex + 1;
        var maxDay = DaysInMonth(year, month);
        if (date.Day > maxDay && policy == MonthEndPolicy.Skip) throw new InvalidOperationException("The target month does not contain the source day.");
        return new(year, month, Math.Min(date.Day, maxDay));
    }
    private void Validate(PersianDate date)
    {
        if (date.Year < 1 || date.Month is < 1 or > 12 || date.Day < 1 || date.Day > DaysInMonth(date.Year, date.Month))
            throw new ArgumentOutOfRangeException(nameof(date), "Persian date is invalid.");
    }
}