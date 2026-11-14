using Hesabyar.App.Resources.Strings;
using Hesabyar.App.Services;
using Hesabyar.App.Views;
using Hesabyar.Infrastructure.Persistence;
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
    private readonly DeviceDatabaseKeyProvider _databaseKeyProvider;
    private bool _isInitializing;

    public StartupPage(DatabaseInitializer databaseInitializer, IServiceProvider services,
        ILocalizationService localization, AppLockService appLock, BackupService backupService,
        DeviceDatabaseKeyProvider databaseKeyProvider)
    {
        InitializeComponent();
        _databaseInitializer = databaseInitializer;
        _services = services;
        _appLock = appLock;
        _backupService = backupService;
        _databaseKeyProvider = databaseKeyProvider;
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
            await _databaseKeyProvider.InitializeAsync(backupDirectory);
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