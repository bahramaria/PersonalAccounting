namespace Hesabyar.Localization.Calendar;
public readonly record struct PersianDate(int Year, int Month, int Day);
public enum MonthEndPolicy { Clamp, Skip }