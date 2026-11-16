using Hesabyar.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Hesabyar.App.Services;

public sealed class ReminderNotificationSyncService(
    IServiceScopeFactory scopeFactory,
    IReminderNotificationScheduler scheduler,
    TimeProvider timeProvider)
{
    public async Task SyncAsync(CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var reminders = await scope.ServiceProvider.GetRequiredService<ReminderService>()
            .GetAsync(includeCompleted: true, cancellationToken: cancellationToken);
        var now = timeProvider.GetUtcNow();

        foreach (var reminder in reminders)
        {
            try
            {
                if (!reminder.IsCompleted && reminder.NotifyAtUtc is { } notifyAt && notifyAt > now)
                    await scheduler.ScheduleAsync(reminder.Id, reminder.Title, notifyAt, cancellationToken);
                else
                    await scheduler.CancelAsync(reminder.Id, cancellationToken);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // A platform notification failure must not prevent access to the financial data.
            }
        }
    }

    public Task CancelAsync(Guid reminderId, CancellationToken cancellationToken = default) =>
        scheduler.CancelAsync(reminderId, cancellationToken);
}
