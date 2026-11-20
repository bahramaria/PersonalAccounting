namespace Hesabyar.Localization.Calendar;

public enum CalendarKind { Persian, Gregorian }

public interface IDateCalendarService
{
    CalendarKind CurrentCalendar { get; set; }
    string Format(DateOnly date);
    bool TryParse(string? value, out DateOnly date);
}

public sealed class DateCalendarService(ICalendarService persianCalendar) : IDateCalendarService
{
    public CalendarKind CurrentCalendar { get; set; } = CalendarKind.Persian;

    public string Format(DateOnly date)
    {
        if (CurrentCalendar == CalendarKind.Gregorian)
            return date.ToString("yyyy/MM/dd", System.Globalization.CultureInfo.InvariantCulture);
        var persian = persianCalendar.ToPersian(date);
        return $"{persian.Year:0000}/{persian.Month:00}/{persian.Day:00}";
    }

    public bool TryParse(string? value, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var normalized = NormalizeDigits(value.Trim()).Replace('-', '/');
        var parts = normalized.Split('/');
        if (parts.Length != 3 ||
            !int.TryParse(parts[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var year) ||
            !int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var month) ||
            !int.TryParse(parts[2], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var day))
            return false;

        if (CurrentCalendar == CalendarKind.Gregorian)
        {
            try { date = new DateOnly(year, month, day); return true; }
            catch (ArgumentOutOfRangeException) { return false; }
        }
        try { date = persianCalendar.ToGregorian(new PersianDate(year, month, day)); return true; }
        catch (ArgumentException) { return false; }
    }

    private static string NormalizeDigits(string value)
    {
        var result = new System.Text.StringBuilder(value.Length);
        foreach (var character in value)
            result.Append(character switch
            {
                >= '۰' and <= '۹' => (char)('0' + character - '۰'),
                >= '٠' and <= '٩' => (char)('0' + character - '٠'),
                _ => character
            });
        return result.ToString();
    }
}
