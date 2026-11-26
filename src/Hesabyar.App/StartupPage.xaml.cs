using Hesabyar.Application.Accounts;
using Hesabyar.App.Resources.Strings;
using Hesabyar.App.Services;
using Hesabyar.App.Views;
using Hesabyar.Domain.Accounts;
using Hesabyar.Infrastructure.Persistence;
using Hesabyar.Infrastructure.Security;
using Hesabyar.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Storage;

namespace Hesabyar.App;

public partial class StartupPage : ContentPage
{
    private readonly DatabaseInitializer _databaseInitializer;
    private readonly IServiceProvider _services;
    private readonly AppLockService _appLock;
    private readonly BackupService _backupService;
    private readonly IDatabaseKeyInitializer _databaseKeyInitializer;
    private readonly TimeProvider _timeProvider;
    private bool _isInitializing;

    public StartupPage(DatabaseInitializer databaseInitializer, IServiceProvider services,
        ILocalizationService localization, AppLockService appLock, BackupService backupService,
        IDatabaseKeyInitializer databaseKeyInitializer, TimeProvider timeProvider)
    {
        InitializeComponent();
        _databaseInitializer = databaseInitializer;
        _services = services;
        _appLock = appLock;
        _backupService = backupService;
        _databaseKeyInitializer = databaseKeyInitializer;
        _timeProvider = timeProvider;
        FlowDirection = localization.FlowDirection == TextDirection.RightToLeft
            ? Microsoft.Maui.FlowDirection.RightToLeft : Microsoft.Maui.FlowDirection.LeftToRight;
        StatusLabel.Text = AppStrings.StartupInitializing;
        RetryButton.Text = AppStrings.Retry;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await InitializeDatabaseAsync();
    }

    private async void OnRetryClicked(object? sender, EventArgs e) => await InitializeDatabaseAsync();

    private async Task InitializeDatabaseAsync()
    {
        if (_isInitializing) return;
        _isInitializing = true;
        LoadingIndicator.IsVisible = true;
        LoadingIndicator.IsRunning = true;
        RetryButton.IsVisible = false;
        StatusLabel.Text = AppStrings.StartupInitializing;
        try
        {
            var backupDirectory = Path.Combine(FileSystem.AppDataDirectory, "migration-backups");
            await _databaseKeyInitializer.InitializeAsync(backupDirectory);
            await _databaseInitializer.InitializeAsync(backupDirectory);
            try
            {
                await _services.GetRequiredService<RecurringTransactionService>()
                    .PrepareDueOccurrencesAsync(DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime));
                await _services.GetRequiredService<ReminderNotificationSyncService>().SyncAsync();
            }
            catch
            {
                // Scheduled work is retried on the next app activation.
            }
            try
            {
                await _backupService.CreateDailyAutomaticBackupAsync(
                    Path.Combine(FileSystem.AppDataDirectory, "backups", "automatic"));
            }
            catch (Exception)
            {
                // Automatic backup failure must not prevent access to the application.
            }

            var window = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault();
            if (window is null) throw new InvalidOperationException("The application window is not available.");

            // The database is the source of truth; a stale device preference must not skip onboarding.
            var hasUserAccounts = await HasUserAccountsAsync();
            if (!hasUserAccounts)
                window.Page = _services.GetRequiredService<OnboardingPage>();
            else
                window.Page = await _appLock.IsPinEnabledAsync()
                    ? _services.GetRequiredService<PinLockPage>()
                    : _services.GetRequiredService<AppShell>();
        }
        catch (Exception)
        {
            LoadingIndicator.IsRunning = false;
            LoadingIndicator.IsVisible = false;
            StatusLabel.Text = AppStrings.StartupFailed;
            RetryButton.IsVisible = true;
        }
        finally { _isInitializing = false; }
    }

    private async Task<bool> HasUserAccountsAsync()
    {
        using var scope = _services.CreateScope();
        var accountService = scope.ServiceProvider.GetRequiredService<AccountService>();
        var result = await accountService.GetAccountsAsync(includeArchived: true);
        if (!result.IsSuccess || result.Value is null)
            throw new InvalidOperationException("The existing account state could not be verified.");

        return result.Value.Any(item =>
            item.Account.Kind is AccountKind.Cash or AccountKind.Bank or AccountKind.Card
                or AccountKind.Wallet or AccountKind.CreditCard);
    }
}
