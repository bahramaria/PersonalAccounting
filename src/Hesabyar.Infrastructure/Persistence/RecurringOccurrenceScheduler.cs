using Hesabyar.Application.Common;
using Hesabyar.Application.Recurring;
using Hesabyar.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Hesabyar.Infrastructure.Persistence;

public sealed class RecurringOccurrenceScheduler(
    IDbContextFactory<AppDbContext> contextFactory,
    IClock clock)
{
    public async Task<int> PrepareDueOccurrencesAsync(
        DateOnly throughDate,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var schedules = await db.RecurringTransactions
            .Where(x => !x.IsPaused && x.NextOccurrenceDate <= throughDate)
            .OrderBy(x => x.NextOccurrenceDate)
            .ToListAsync(cancellationToken);

        var preparedCount = 0;
        foreach (var schedule in schedules)
        {
            if (!Enum.TryParse<RecurrenceIntervalUnit>(schedule.IntervalUnit, true, out var unit))
                throw new InvalidOperationException($"Unsupported recurrence interval unit: {schedule.IntervalUnit}");

            var occurrenceDate = schedule.NextOccurrenceDate;
            while (occurrenceDate <= throughDate &&
                   (schedule.EndDate is null || occurrenceDate <= schedule.EndDate.Value))
            {
                var exists = await db.RecurringTransactionOccurrences.AnyAsync(
                    x => x.RecurringTransactionId == schedule.Id && x.OccurrenceDate == occurrenceDate,
                    cancellationToken);

                if (!exists)
                {
                    db.RecurringTransactionOccurrences.Add(new DbRecurringTransactionOccurrence
                    {
                        RecurringTransactionId = schedule.Id,
                        OccurrenceDate = occurrenceDate,
                        Status = "Pending"
                    });
                    preparedCount++;
                }

                occurrenceDate = RecurrenceCalculator.GetNextOccurrence(
                    schedule.StartDate, occurrenceDate, schedule.IntervalCount, unit);
            }

            schedule.NextOccurrenceDate = occurrenceDate;
            schedule.LastGeneratedAtUtc = clock.UtcNow;
            if (schedule.EndDate is not null && occurrenceDate > schedule.EndDate.Value)
                schedule.IsPaused = true;
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return preparedCount;
    }
}