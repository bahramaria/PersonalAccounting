using Hesabyar.App.Services;
using Hesabyar.App.Views;
using Hesabyar.Infrastructure.Persistence;
using Hesabyar.Localization.Calendar;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Storage;

namespace Hesabyar.App;

public partial class App : Microsoft.Maui.Controls.Application
{
    private readonly IServiceProvider _services;
    private DateTimeOffset? _backgroundedAt;

    public App(IServiceProvider services)
    {
        InitializeComponent();
        AppLocalization.Initialize();
        _services = services;
        services.GetRequiredService<IDateCalendarService>().CurrentCalendar =
            Preferences.Get("app.calendar", "persian") == "gregorian" ? CalendarKind.Gregorian : CalendarKind.Persian;
        UserAppTheme = Preferences.Get("app.theme", "system") switch
        {
            "light" => AppTheme.Light,
            "dark" => AppTheme.Dark,
            _ => AppTheme.Unspecified
        };
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(_services.GetRequiredService<StartupPage>());
        window.Stopped += (_, _) => _backgroundedAt = DateTimeOffset.UtcNow;
        window.Resumed += OnWindowResumed;
        return window;
    }

    private async void OnWindowResumed(object? sender, EventArgs e)
    {
        await PrepareRecurringOccurrencesAsync();
        await SyncReminderNotificationsAsync();
        if (_backgroundedAt is not { } backgroundedAt) return;
        var elapsed = DateTimeOffset.UtcNow - backgroundedAt;
        _backgroundedAt = null;
        var autoLockSeconds = Preferences.Get("security.auto-lock-seconds", 0);
        if (elapsed < TimeSpan.FromSeconds(autoLockSeconds)) return;

        if (!await _services.GetRequiredService<AppLockService>().IsPinEnabledAsync()) return;
        var window = Windows.FirstOrDefault();
        if (window is not null && window.Page is not PinLockPage)
            window.Page = _services.GetRequiredService<PinLockPage>();
    }

    private async Task SyncReminderNotificationsAsync()
    {
        try
        {
            await _services.GetRequiredService<ReminderNotificationSyncService>().SyncAsync();
        }
        catch
        {
            // Notification synchronization is retried on the next app activation.
        }
    }

    private async Task PrepareRecurringOccurrencesAsync()
    {
        try
        {
            using var scope = _services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<RecurringTransactionService>()
                .PrepareDueOccurrencesAsync(DateOnly.FromDateTime(DateTime.Today));
        }
        catch
        {
            // Recurring occurrence preparation is retried on the next app activation.
        }
    }
}