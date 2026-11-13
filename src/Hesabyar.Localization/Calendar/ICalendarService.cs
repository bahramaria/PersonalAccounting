namespace Hesabyar.Localization.Calendar;
public interface ICalendarService
{
    PersianDate ToPersian(DateOnly gregorian);
    DateOnly ToGregorian(PersianDate persian);
    bool IsLeapYear(int persianYear);
    int DaysInMonth(int persianYear, int month);
    PersianDate AddMonths(PersianDate date, int months, MonthEndPolicy policy = MonthEndPolicy.Clamp);
}