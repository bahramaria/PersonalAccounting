using System.Globalization;
using Android.Provider;
using Hesabyar.Infrastructure.Persistence;

namespace Hesabyar.App;

public sealed record SmsHistoryImportSummary(int Scanned, int Added, int Duplicates, int Ignored);

public sealed class AndroidSmsHistoryImporter
{
    private const int MaximumMessages = 500;
    private static readonly TimeSpan Lookback = TimeSpan.FromDays(90);

    public async Task<SmsHistoryImportSummary> ImportRecentAsync(
        BankSmsInboxService inbox, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inbox);
        var cutoff = DateTimeOffset.UtcNow.Subtract(Lookback).ToUnixTimeMilliseconds()
            .ToString(CultureInfo.InvariantCulture);
        var projection = new[] { "address", "body" };
        using var cursor = Platform.AppContext.ContentResolver?.Query(
            Telephony.Sms.Inbox.ContentUri,
            projection,
            "date >= ?",
            [cutoff],
            "date DESC");
        if (cursor is null)
            return new SmsHistoryImportSummary(0, 0, 0, 0);

        var addressIndex = cursor.GetColumnIndex("address");
        var bodyIndex = cursor.GetColumnIndex("body");
        if (addressIndex < 0 || bodyIndex < 0)
            return new SmsHistoryImportSummary(0, 0, 0, 0);

        var scanned = 0;
        var added = 0;
        var duplicates = 0;
        var ignored = 0;
        while (scanned < MaximumMessages && cursor.MoveToNext())
        {
            cancellationToken.ThrowIfCancellationRequested();
            scanned++;
            var sender = cursor.GetString(addressIndex) ?? string.Empty;
            var body = cursor.GetString(bodyIndex) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(body))
            {
                ignored++;
                continue;
            }

            var outcome = await inbox.ImportAsync(sender, body, cancellationToken);
            switch (outcome)
            {
                case SmsImportOutcome.Added: added++; break;
                case SmsImportOutcome.Duplicate: duplicates++; break;
                default: ignored++; break;
            }
        }

        return new SmsHistoryImportSummary(scanned, added, duplicates, ignored);
    }
}
