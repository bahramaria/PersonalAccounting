using Hesabyar.App.Resources.Strings;
using Hesabyar.App.Services;
using Hesabyar.App.Views;
using Hesabyar.Application.Accounts;
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
    private bool _isInitializing;

    public StartupPage(DatabaseInitializer databaseInitializer, IServiceProvider services,
        ILocalizationService localization, AppLockService appLock, BackupService backupService,
        IDatabaseKeyInitializer databaseKeyInitializer)
    {
        InitializeComponent();
        _databaseInitializer = databaseInitializer;
        _services = services;
        _appLock = appLock;
        _backupService = backupService;
        _databaseKeyInitializer = databaseKeyInitializer;
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
                await _backupService.CreateDailyAutomaticBackupAsync(
                    Path.Combine(FileSystem.AppDataDirectory, "backups", "automatic"));
            }
            catch (IOException)
            {
                // Automatic backup failure must not prevent access to the application.
            }
            var window = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault();
            if (window is null) throw new InvalidOperationException("The application window is not available.");

            if (!Preferences.Get("onboarding.completed", false))
            {
                using var scope = _services.GetRequiredService<IServiceScopeFactory>().CreateScope();
                var accounts = await scope.ServiceProvider.GetRequiredService<AccountService>()
                    .GetAccountsAsync(includeArchived: true);
                if (!accounts.IsSuccess || accounts.Value is null)
                    throw new InvalidOperationException("Accounts could not be checked before onboarding.");

                var hasUserAccount = accounts.Value.Any(x =>
                    x.Account.Kind is AccountKind.Cash or AccountKind.Bank or AccountKind.Card
                        or AccountKind.Wallet or AccountKind.CreditCard);
                if (hasUserAccount)
                    Preferences.Set("onboarding.completed", true);
            }

            if (!Preferences.Get("onboarding.completed", false))
            {
                window.Page = _services.GetRequiredService<OnboardingPage>();
                return;
            }

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
}