namespace Hesabyar.App.Services;

public interface IReminderNotificationScheduler
{
    Task ScheduleAsync(Guid reminderId, string title, DateTimeOffset notifyAtUtc, CancellationToken cancellationToken = default);
    Task CancelAsync(Guid reminderId, CancellationToken cancellationToken = default);
}
