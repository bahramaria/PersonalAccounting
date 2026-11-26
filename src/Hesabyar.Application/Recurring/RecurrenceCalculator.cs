namespace Hesabyar.Application.Recurring;

public static class RecurrenceCalculator
{
    public static DateOnly GetNextOccurrence(
        DateOnly startDate,
        DateOnly currentOccurrence,
        int intervalCount,
        RecurrenceIntervalUnit unit)
    {
        if (intervalCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(intervalCount));

        if (currentOccurrence < startDate)
            throw new ArgumentOutOfRangeException(nameof(currentOccurrence));

        var intervalIndex = 1;
        DateOnly candidate;

        do
        {
            candidate = AddIntervals(startDate, checked(intervalCount * intervalIndex), unit);
            intervalIndex = checked(intervalIndex + 1);
        }
        while (candidate <= currentOccurrence);

        return candidate;
    }

    public static DateOnly AddIntervals(DateOnly date, int count, RecurrenceIntervalUnit unit)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));

        return unit switch
        {
            RecurrenceIntervalUnit.Day => date.AddDays(count),
            RecurrenceIntervalUnit.Week => date.AddDays(checked(count * 7)),
            RecurrenceIntervalUnit.Month => date.AddMonths(count),
            RecurrenceIntervalUnit.Year => date.AddYears(count),
            _ => throw new ArgumentOutOfRangeException(nameof(unit))
        };
    }
}