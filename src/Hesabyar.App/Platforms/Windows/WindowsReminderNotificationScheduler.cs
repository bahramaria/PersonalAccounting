using Hesabyar.App.Services;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Hesabyar.App;

public sealed class WindowsReminderNotificationScheduler : IReminderNotificationScheduler
{
    private const string AppUserModelId = "com.example.hesabyar";

    public Task ScheduleAsync(Guid reminderId, string title, DateTimeOffset notifyAtUtc, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var notifier = ToastNotificationManager.CreateToastNotifier(AppUserModelId);
        RemoveExisting(notifier, reminderId);
        if (notifyAtUtc <= DateTimeOffset.UtcNow)
            return Task.CompletedTask;

        var escapedTitle = System.Security.SecurityElement.Escape(title) ?? "Hesabyar";
        var xml = new XmlDocument();
        xml.LoadXml($"<toast><visual><binding template='ToastGeneric'><text>{escapedTitle}</text><text>A reminder is due.</text></binding></visual></toast>");
        var scheduled = new ScheduledToastNotification(xml, notifyAtUtc.LocalDateTime)
        {
            Tag = reminderId.ToString("N"),
            Group = "reminders"
        };
        notifier.AddToSchedule(scheduled);
        return Task.CompletedTask;
    }

    public Task CancelAsync(Guid reminderId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RemoveExisting(ToastNotificationManager.CreateToastNotifier(AppUserModelId), reminderId);
        return Task.CompletedTask;
    }

    private static void RemoveExisting(ToastNotifier notifier, Guid reminderId)
    {
        var tag = reminderId.ToString("N");
        foreach (var scheduled in notifier.GetScheduledToastNotifications())
        {
            if (scheduled.Tag == tag && scheduled.Group == "reminders")
                notifier.RemoveFromSchedule(scheduled);
        }
    }
}
