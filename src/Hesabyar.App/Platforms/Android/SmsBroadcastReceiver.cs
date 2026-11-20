using Android;
using Android.App;
using Android.Content;
using Android.Provider;
using Android.Telephony;
using Hesabyar.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Storage;

namespace Hesabyar.App;

[BroadcastReceiver(Enabled = true, Exported = true, Permission = Manifest.Permission.BroadcastSms)]
[IntentFilterAttribute(new[] { "android.provider.Telephony.SMS_RECEIVED" })]
public sealed class SmsBroadcastReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent?.Action != "android.provider.Telephony.SMS_RECEIVED" ||
            !Preferences.Default.Get("sms-auto-import-enabled", false))
            return;

        var messages = Telephony.Sms.Intents.GetMessagesFromIntent(intent);
        if (messages is null || messages.Length == 0)
            return;

        var sender = messages[0].OriginatingAddress ?? string.Empty;
        var body = string.Concat(messages.Select(message => message.MessageBody));
        if (string.IsNullOrWhiteSpace(body))
            return;

        var pendingResult = GoAsync();
        _ = Task.Run(async () =>
        {
            try
            {
                var platform = IPlatformApplication.Current;
                if (platform is null) return;
                using var scope = platform.Services.CreateScope();
                var inbox = scope.ServiceProvider.GetRequiredService<BankSmsInboxService>();
                var receivedAtUtc = messages[0].TimestampMillis > 0
                    ? DateTimeOffset.FromUnixTimeMilliseconds(messages[0].TimestampMillis)
                    : (DateTimeOffset?)null;
                await inbox.ImportAsync(sender, body, receivedAtUtc: receivedAtUtc);
            }
            catch
            {
                // Incoming SMS must not interrupt the system broadcast.
            }
            finally
            {
                pendingResult?.Finish();
            }
        });
    }
}
