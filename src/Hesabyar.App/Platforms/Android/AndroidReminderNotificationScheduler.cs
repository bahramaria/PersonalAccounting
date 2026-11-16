using Android.App;
using Android.Content;
using Android.OS;
using Hesabyar.App.Resources.Strings;

namespace Hesabyar.App;

public sealed class AndroidReminderNotificationScheduler : Services.IReminderNotificationScheduler
{
    public Task ScheduleAsync(Guid reminderId, string title, DateTimeOffset notifyAtUtc, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = Android.App.Application.Context;
        var preferences = context.GetSharedPreferences("hesabyar.reminders", FileCreationMode.Private);
        var prefix = $"reminder:{reminderId:N}";
        preferences?.Edit()
            ?.PutString($"{prefix}:title", title)
            ?.PutLong($"{prefix}:at", notifyAtUtc.ToUnixTimeMilliseconds())
            ?.Apply();

        ScheduleAlarm(context, reminderId, title, notifyAtUtc);
        return Task.CompletedTask;
    }

    public Task CancelAsync(Guid reminderId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = Android.App.Application.Context;
        CancelAlarm(context, reminderId);
        var prefix = $"reminder:{reminderId:N}";
        context.GetSharedPreferences("hesabyar.reminders", FileCreationMode.Private)
            ?.Edit()
            ?.Remove($"{prefix}:title")
            ?.Remove($"{prefix}:at")
            ?.Apply();
        return Task.CompletedTask;
    }

    internal static void ScheduleAlarm(Context context, Guid reminderId, string title, DateTimeOffset notifyAtUtc)
    {
        if (notifyAtUtc <= DateTimeOffset.UtcNow)
            return;

        var intent = new Intent(context, typeof(ReminderAlarmReceiver));
        intent.PutExtra(ReminderAlarmReceiver.ReminderIdExtra, reminderId.ToString("N"));
        intent.PutExtra(ReminderAlarmReceiver.ReminderTitleExtra, title);
        var pendingIntent = PendingIntent.GetBroadcast(
            context,
            GetRequestCode(reminderId),
            intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        if (pendingIntent is null)
            return;
        var alarmManager = context.GetSystemService(Context.AlarmService) as AlarmManager;
        if (alarmManager is null)
            return;

        var triggerAt = notifyAtUtc.ToUnixTimeMilliseconds();
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            if (alarmManager.CanScheduleExactAlarms())
                alarmManager.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, triggerAt, pendingIntent);
            else
                alarmManager.SetAndAllowWhileIdle(AlarmType.RtcWakeup, triggerAt, pendingIntent);
        }
        else
        {
            alarmManager.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, triggerAt, pendingIntent);
        }
    }

    internal static void CancelAlarm(Context context, Guid reminderId)
    {
        var intent = new Intent(context, typeof(ReminderAlarmReceiver));
        var pendingIntent = PendingIntent.GetBroadcast(
            context,
            GetRequestCode(reminderId),
            intent,
            PendingIntentFlags.NoCreate | PendingIntentFlags.Immutable);
        if (pendingIntent is null)
            return;

        (context.GetSystemService(Context.AlarmService) as AlarmManager)?.Cancel(pendingIntent);
        pendingIntent.Cancel();
    }

    internal static int GetRequestCode(Guid reminderId) =>
        BitConverter.ToInt32(reminderId.ToByteArray(), 0) & int.MaxValue;
}

[BroadcastReceiver(Enabled = true, Exported = false)]
public sealed class ReminderAlarmReceiver : BroadcastReceiver
{
    internal const string ReminderIdExtra = "reminder_id";
    internal const string ReminderTitleExtra = "reminder_title";
    private const string ChannelId = "hesabyar_reminders";

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent is null ||
            !Guid.TryParseExact(intent.GetStringExtra(ReminderIdExtra), "N", out var reminderId))
            return;

        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu &&
            context.CheckSelfPermission(Android.Manifest.Permission.PostNotifications) != Android.Content.PM.Permission.Granted)
            return;

        var manager = context.GetSystemService(Context.NotificationService) as NotificationManager;
        if (manager is null)
            return;

        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            manager.CreateNotificationChannel(new NotificationChannel(
                ChannelId, AppStrings.ReminderNotificationChannel, NotificationImportance.Default));

        var title = intent.GetStringExtra(ReminderTitleExtra) ?? "Hesabyar";
        var builder = Build.VERSION.SdkInt >= BuildVersionCodes.O
            ? new Notification.Builder(context, ChannelId)
            : new Notification.Builder(context);
        var notification = builder
            .SetContentTitle(title)
            .SetContentText(AppStrings.ReminderNotificationBody)
            .SetSmallIcon(Android.Resource.Drawable.IcDialogInfo)
            .SetAutoCancel(true)
            .Build();
        manager.Notify(AndroidReminderNotificationScheduler.GetRequestCode(reminderId), notification);
    }
}

[BroadcastReceiver(Enabled = true, Exported = true)]
[IntentFilter(new[] { Intent.ActionBootCompleted })]
public sealed class ReminderBootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.Action != Intent.ActionBootCompleted)
            return;

        var preferences = context.GetSharedPreferences("hesabyar.reminders", FileCreationMode.Private);
        if (preferences is null)
            return;

        foreach (var entry in preferences.All)
        {
            if (string.IsNullOrEmpty(entry.Key) || !entry.Key.EndsWith(":at", StringComparison.Ordinal) ||
                !long.TryParse(entry.Value?.ToString(), out var timestamp))
                continue;

            var idText = entry.Key["reminder:".Length..^":at".Length];
            if (!Guid.TryParseExact(idText, "N", out var reminderId))
                continue;

            var title = preferences.GetString($"reminder:{idText}:title", "Hesabyar") ?? "Hesabyar";
            AndroidReminderNotificationScheduler.ScheduleAlarm(
                context, reminderId, title, DateTimeOffset.FromUnixTimeMilliseconds(timestamp));
        }
    }
}
