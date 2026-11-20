using Hesabyar.Application.Recurring;

namespace Hesabyar.Infrastructure.Tests;

public sealed class RecurrenceCalculatorTests
{
    [Fact]
    public void MonthlySchedule_PreservesEndOfMonthAnchor()
    {
        var start = new DateOnly(2026, 1, 31);

        var february = RecurrenceCalculator.GetNextOccurrence(start, start, 1, RecurrenceIntervalUnit.Month);
        var march = RecurrenceCalculator.GetNextOccurrence(start, february, 1, RecurrenceIntervalUnit.Month);

        Assert.Equal(new DateOnly(2026, 2, 28), february);
        Assert.Equal(new DateOnly(2026, 3, 31), march);
    }

    [Fact]
    public void MultiMonthSchedule_UsesOriginalStartDateAsAnchor()
    {
        var start = new DateOnly(2026, 1, 31);

        var next = RecurrenceCalculator.GetNextOccurrence(
            start, new DateOnly(2026, 2, 1), 2, RecurrenceIntervalUnit.Month);

        Assert.Equal(new DateOnly(2026, 3, 31), next);
    }

    [Fact]
    public void WeeklySchedule_AdvancesByConfiguredNumberOfWeeks()
    {
        var start = new DateOnly(2026, 11, 16);

        var next = RecurrenceCalculator.GetNextOccurrence(
            start, start, 2, RecurrenceIntervalUnit.Week);

        Assert.Equal(new DateOnly(2026, 11, 30), next);
    }

    [Fact]
    public void YearlySchedule_HandlesLeapDay()
    {
        var start = new DateOnly(2024, 2, 29);

        var next = RecurrenceCalculator.GetNextOccurrence(
            start, start, 1, RecurrenceIntervalUnit.Year);

        Assert.Equal(new DateOnly(2025, 2, 28), next);
    }

    [Fact]
    public void RejectsCurrentOccurrenceBeforeStartDate()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RecurrenceCalculator.GetNextOccurrence(
                new DateOnly(2026, 2, 1), new DateOnly(2026, 1, 31),
                1, RecurrenceIntervalUnit.Month));
    }

    [Fact]
    public void RejectsNonPositiveInterval()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RecurrenceCalculator.GetNextOccurrence(
                new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1),
                0, RecurrenceIntervalUnit.Day));
    }
}