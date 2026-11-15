using Hesabyar.Application.Security;
using Hesabyar.App.Resources.Strings;
using Hesabyar.App.Services;
using Hesabyar.Localization;
using Hesabyar.Localization.Calendar;
using Microsoft.Maui.Storage;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Hesabyar.Infrastructure.Persistence;

namespace Hesabyar.App.Views;

public sealed class SettingsPage : ContentPage
{
    private readonly IServiceProvider _services;
    private readonly AppLockService _appLock;
    private readonly IDateCalendarService _calendar;
    private readonly Button _languageButton = new() { HorizontalOptions = LayoutOptions.Fill };
    private readonly Button _themeButton = new() { HorizontalOptions = LayoutOptions.Fill };
    private readonly Button _calendarButton = new() { HorizontalOptions = LayoutOptions.Fill };
    private readonly Button _pinButton = new() { HorizontalOptions = LayoutOptions.Fill };
    private readonly Button _autoLockButton = new() { HorizontalOptions = LayoutOptions.Fill };
    private readonly Button _backupButton = new() { HorizontalOptions = LayoutOptions.Fill };
    private readonly Button _restoreButton = new() { HorizontalOptions = LayoutOptions.Fill };
    private readonly BackupService _backupService;

    public SettingsPage(IServiceProvider services, ILocalizationService localization,
        IDateCalendarService calendar, AppLockService appLock, BackupService backupService)
    {
        _services = services;
        _calendar = calendar;
        _appLock = appLock;
        _backupService = backupService;
        Title = AppStrings.SettingsHeading;
        FlowDirection = localization.FlowDirection == TextDirection.RightToLeft
            ? Microsoft.Maui.FlowDirection.RightToLeft : Microsoft.Maui.FlowDirection.LeftToRight;
        _languageButton.Clicked += OnLanguageClicked;
        _themeButton.Clicked += OnThemeClicked;
        _calendarButton.Clicked += OnCalendarClicked;
        _pinButton.Clicked += OnPinClicked;
        _autoLockButton.Clicked += OnAutoLockClicked;
        _backupButton.Clicked += OnBackupClicked;
        _restoreButton.Clicked += OnRestoreClicked;
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 24, Spacing = 16,
                Children =
                {
                    new Label { Text = AppStrings.SettingsHeading, FontSize = 24, FontAttributes = FontAttributes.Bold },
                    _languageButton, _themeButton, _calendarButton, _pinButton, _autoLockButton,
                    new Label { Text = AppStrings.BackupHeading, FontSize = 20, FontAttributes = FontAttributes.Bold },
                    _backupButton, _restoreButton
                }
            }
        };
        RefreshLabels();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RefreshPinLabelAsync();
    }

    private async void OnLanguageClicked(object? sender, EventArgs e)
    {
        var selected = await DisplayActionSheetAsync(AppStrings.SettingsLanguage, AppStrings.Cancel, null,
            AppStrings.SettingsLanguagePersian, AppStrings.SettingsLanguageEnglish);
        if (selected == AppStrings.SettingsLanguagePersian) Preferences.Set("app.language", "fa-IR");
        else if (selected == AppStrings.SettingsLanguageEnglish) Preferences.Set("app.language", "en-US");
        else return;
        AppLocalization.Initialize();
        var window = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault();
        if (window is not null) window.Page = _services.GetRequiredService<AppShell>();
    }

    private async void OnThemeClicked(object? sender, EventArgs e)
    {
        var selected = await DisplayActionSheetAsync(AppStrings.SettingsTheme, AppStrings.Cancel, null,
            AppStrings.SettingsThemeSystem, AppStrings.SettingsThemeLight, AppStrings.SettingsThemeDark);
        var theme = selected switch
        {
            var value when value == AppStrings.SettingsThemeSystem => "system",
            var value when value == AppStrings.SettingsThemeLight => "light",
            var value when value == AppStrings.SettingsThemeDark => "dark",
            _ => null
        };
        if (theme is null) return;
        Preferences.Set("app.theme", theme);
        if (Microsoft.Maui.Controls.Application.Current is App app)
            app.UserAppTheme = theme switch
            {
                "light" => AppTheme.Light,
                "dark" => AppTheme.Dark,
                _ => AppTheme.Unspecified
            };
        RefreshLabels();
    }

    private async void OnCalendarClicked(object? sender, EventArgs e)
    {
        var selected = await DisplayActionSheetAsync(AppStrings.SettingsCalendar, AppStrings.Cancel, null,
            AppStrings.SettingsCalendarPersian, AppStrings.SettingsCalendarGregorian);
        if (selected == AppStrings.SettingsCalendarPersian) Preferences.Set("app.calendar", "persian");
        else if (selected == AppStrings.SettingsCalendarGregorian) Preferences.Set("app.calendar", "gregorian");
        else return;
        _calendar.CurrentCalendar = Preferences.Get("app.calendar", "persian") == "gregorian"
            ? CalendarKind.Gregorian : CalendarKind.Persian;
        RefreshLabels();
    }

    private async void OnPinClicked(object? sender, EventArgs e)
    {
        var enabled = await _appLock.IsPinEnabledAsync();
        var selected = enabled
            ? await DisplayActionSheetAsync(AppStrings.PinManage, AppStrings.Cancel, null, AppStrings.PinChange, AppStrings.PinDisable)
            : await DisplayActionSheetAsync(AppStrings.PinManage, AppStrings.Cancel, null, AppStrings.PinEnable);

        if (selected == AppStrings.PinEnable && !enabled)
        {
            if (!await DisplayAlertAsync(AppStrings.PinSetupWarning, AppStrings.PinRecoveryUnavailable,
                    AppStrings.Continue, AppStrings.Cancel)) return;
            var newPin = await PromptPinAsync(AppStrings.PinEnterNew);
            if (newPin is null) return;
            var confirmation = await PromptPinAsync(AppStrings.PinConfirm);
            if (confirmation is null) return;
            if (!PinHasher.IsValidPin(newPin))
            {
                await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.PinInvalidFormat, AppStrings.Ok);
                return;
            }
            if (PinHasher.NormalizePin(newPin) != PinHasher.NormalizePin(confirmation))
            {
                await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.PinMismatch, AppStrings.Ok);
                return;
            }
            await _appLock.SetPinAsync(newPin);
            await DisplayAlertAsync(AppStrings.PinSettings, AppStrings.PinEnabled, AppStrings.Ok);
            var window = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault();
            if (window is not null)
                window.Page = _services.GetRequiredService<PinLockPage>();
            return;
        }
        else if (selected == AppStrings.PinChange && enabled)
        {
            var currentPin = await PromptPinAsync(AppStrings.PinEnterCurrent);
            if (currentPin is null) return;
            var verification = await _appLock.VerifyPinAsync(currentPin);
            if (!verification.IsValid)
            {
                await DisplayAlertAsync(AppStrings.PinSettings, GetVerificationMessage(verification), AppStrings.Ok);
                return;
            }
            var newPin = await PromptPinAsync(AppStrings.PinEnterNew);
            if (newPin is null) return;
            var confirmation = await PromptPinAsync(AppStrings.PinConfirm);
            if (confirmation is null) return;
            if (!PinHasher.IsValidPin(newPin))
            {
                await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.PinInvalidFormat, AppStrings.Ok);
                return;
            }
            if (PinHasher.NormalizePin(newPin) != PinHasher.NormalizePin(confirmation))
            {
                await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.PinMismatch, AppStrings.Ok);
                return;
            }
            await _appLock.SetPinAsync(newPin);
            await DisplayAlertAsync(AppStrings.PinSettings, AppStrings.PinChanged, AppStrings.Ok);
        }
        else if (selected == AppStrings.PinDisable && enabled)
        {
            var currentPin = await PromptPinAsync(AppStrings.PinEnterCurrent);
            if (currentPin is null) return;
            var result = await _appLock.DisablePinAsync(currentPin);
            await DisplayAlertAsync(AppStrings.PinSettings, result ? AppStrings.PinDisabled : AppStrings.PinInvalid, AppStrings.Ok);
        }
        await RefreshPinLabelAsync();
    }


    private async void OnBackupClicked(object? sender, EventArgs e)
    {
        try
        {
            var password = await PromptSecretAsync(AppStrings.BackupPasswordPrompt, 128);
            if (password is null) return;
            if (password.Length > 0)
            {
                if (password.Length < 8)
                {
                    await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.BackupFailed, AppStrings.Ok);
                    return;
                }
                var confirmation = await PromptSecretAsync(AppStrings.BackupPasswordConfirm, 128);
                if (confirmation is null) return;
                if (!string.Equals(password, confirmation, StringComparison.Ordinal))
                {
                    await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.BackupPasswordMismatch, AppStrings.Ok);
                    return;
                }
            }
            else if (!await DisplayAlertAsync(AppStrings.BackupHeading, AppStrings.BackupUnencryptedWarning,
                         AppStrings.Continue, AppStrings.Cancel))
            {
                return;
            }

            var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            var path = Path.Combine(FileSystem.CacheDirectory, $"hesabyar-{timestamp}.hbak");
            await _backupService.CreateBackupAsync(path, password);
            await DisplayAlertAsync(AppStrings.BackupHeading, AppStrings.BackupCreated, AppStrings.Ok);
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = AppStrings.BackupHeading,
                File = new ShareFile(path)
            });
        }
        catch (Exception)
        {
            await DisplayAlertAsync(AppStrings.BackupHeading, AppStrings.BackupFailed, AppStrings.Ok);
        }
    }

    private async void OnRestoreClicked(object? sender, EventArgs e)
    {
        string? temporaryPath = null;
        try
        {
            var selected = await FilePicker.Default.PickAsync();
            if (selected is null)
            {
                await DisplayAlertAsync(AppStrings.BackupHeading, AppStrings.BackupNoFileSelected, AppStrings.Ok);
                return;
            }

            temporaryPath = Path.Combine(FileSystem.CacheDirectory, $"restore-{Guid.NewGuid():N}.hbak");
            await using (var source = await selected.OpenReadAsync())
            await using (var destination = File.Create(temporaryPath))
                await source.CopyToAsync(destination);

            var password = await PromptSecretAsync(AppStrings.BackupPasswordPrompt, 128);
            if (password is null) return;

            var summary = await _backupService.InspectBackupAsync(temporaryPath, password);
            var details = string.Format(System.Globalization.CultureInfo.CurrentCulture,
                AppStrings.BackupSummaryFormat,
                summary.CreatedAtUtc.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture),
                summary.AppVersion,
                summary.SchemaVersion,
                (summary.DatabaseSizeBytes / 1024d / 1024d).ToString("N2", System.Globalization.CultureInfo.CurrentCulture),
                summary.IsEncrypted ? AppStrings.BackupEncrypted :
                summary.IsDeviceBound ? AppStrings.BackupDeviceBound : AppStrings.BackupNotEncrypted);
            if (!await DisplayAlertAsync(AppStrings.BackupRestorePreview, details, AppStrings.Continue, AppStrings.Cancel))
                return;
            if (!await DisplayAlertAsync(AppStrings.BackupRestore, AppStrings.BackupRestoreConfirmation,
                    AppStrings.Continue, AppStrings.Cancel))
                return;

            await _backupService.RestoreBackupAsync(temporaryPath, password);
            await DisplayAlertAsync(AppStrings.BackupRestore, AppStrings.BackupRestored, AppStrings.Ok);
            var window = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault();
            if (window is not null)
                window.Page = _services.GetRequiredService<StartupPage>();
        }
        catch (UnauthorizedAccessException)
        {
            await DisplayAlertAsync(AppStrings.BackupHeading, AppStrings.BackupWrongPassword, AppStrings.Ok);
        }
        catch (InvalidDataException)
        {
            await DisplayAlertAsync(AppStrings.BackupHeading, AppStrings.BackupInvalid, AppStrings.Ok);
        }
        catch (Exception)
        {
            await DisplayAlertAsync(AppStrings.BackupHeading, AppStrings.BackupFailed, AppStrings.Ok);
        }
        finally
        {
            if (temporaryPath is not null && File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private async void OnAutoLockClicked(object? sender, EventArgs e)
    {
        var selected = await DisplayActionSheetAsync(AppStrings.PinAutoLock, AppStrings.Cancel, null,
            AppStrings.PinAutoLockImmediate, AppStrings.PinAutoLock30Seconds,
            AppStrings.PinAutoLock1Minute, AppStrings.PinAutoLock5Minutes);
        var seconds = selected switch
        {
            var value when value == AppStrings.PinAutoLockImmediate => 0,
            var value when value == AppStrings.PinAutoLock30Seconds => 30,
            var value when value == AppStrings.PinAutoLock1Minute => 60,
            var value when value == AppStrings.PinAutoLock5Minutes => 300,
            _ => -1
        };
        if (seconds >= 0) Preferences.Set("security.auto-lock-seconds", seconds);
        RefreshLabels();
    }

    private Task<string?> PromptPinAsync(string prompt) =>
        PromptSecretAsync(prompt, 8, Keyboard.Numeric, string.Empty);

    private async Task<string?> PromptSecretAsync(
        string prompt, int maxLength, Keyboard? keyboard = null, string? hint = null)
    {
        var promptPage = new SecretPromptPage(prompt, hint ?? AppStrings.BackupPasswordHint,
            AppStrings.Continue, AppStrings.Cancel, maxLength, keyboard);
        await Navigation.PushModalAsync(promptPage);
        return await promptPage.Result;
    }

    private static string GetVerificationMessage(PinVerificationResult result) =>
        result.RetryAfter is { } retry
            ? string.Format(AppStrings.PinLockedForSeconds, Math.Ceiling(retry.TotalSeconds))
            : AppStrings.PinInvalid;

    private async Task RefreshPinLabelAsync()
    {
        var enabled = await _appLock.IsPinEnabledAsync();
        _pinButton.Text = $"{AppStrings.PinSettings}: {(enabled ? AppStrings.PinEnabled : AppStrings.PinDisabled)}";
    }

    private void RefreshLabels()
    {
        var language = Preferences.Get("app.language", "fa-IR") == "en-US"
            ? AppStrings.SettingsLanguageEnglish : AppStrings.SettingsLanguagePersian;
        var theme = Preferences.Get("app.theme", "system") switch
        {
            "light" => AppStrings.SettingsThemeLight,
            "dark" => AppStrings.SettingsThemeDark,
            _ => AppStrings.SettingsThemeSystem
        };
        _languageButton.Text = $"{AppStrings.SettingsLanguage}: {language}";
        _themeButton.Text = $"{AppStrings.SettingsTheme}: {theme}";
        var calendar = _calendar.CurrentCalendar == CalendarKind.Gregorian
            ? AppStrings.SettingsCalendarGregorian : AppStrings.SettingsCalendarPersian;
        _calendarButton.Text = $"{AppStrings.SettingsCalendar}: {calendar}";
        _backupButton.Text = AppStrings.BackupCreate;
        _restoreButton.Text = AppStrings.BackupRestore;
        _autoLockButton.Text = $"{AppStrings.PinAutoLock}: {Preferences.Get("security.auto-lock-seconds", 0) switch
        {
            30 => AppStrings.PinAutoLock30Seconds,
            60 => AppStrings.PinAutoLock1Minute,
            300 => AppStrings.PinAutoLock5Minutes,
            _ => AppStrings.PinAutoLockImmediate
        }}";
    }

    private sealed class SecretPromptPage : ContentPage
    {
        private readonly Entry _entry;
        private readonly TaskCompletionSource<string?> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<string?> Result => _completion.Task;

        public SecretPromptPage(string prompt, string hint, string confirmText, string cancelText,
            int maxLength, Keyboard? keyboard)
        {
            Title = AppStrings.BackupHeading;
            _entry = new Entry
            {
                IsPassword = true,
                MaxLength = maxLength,
                Keyboard = keyboard ?? Keyboard.Default,
                HorizontalOptions = LayoutOptions.Fill
            };

            var confirmButton = new Button { Text = confirmText };
            var cancelButton = new Button { Text = cancelText };
            confirmButton.Clicked += async (_, _) =>
            {
                var value = _entry.Text ?? string.Empty;
                _entry.Text = string.Empty;
                _completion.TrySetResult(value);
                await Navigation.PopModalAsync();
            };
            cancelButton.Clicked += async (_, _) =>
            {
                _entry.Text = string.Empty;
                _completion.TrySetResult(null);
                await Navigation.PopModalAsync();
            };

            Content = new ScrollView
            {
                Content = new VerticalStackLayout
                {
                    Padding = 24,
                    Spacing = 16,
                    VerticalOptions = LayoutOptions.Center,
                    Children =
                    {
                        new Label { Text = prompt, FontAttributes = FontAttributes.Bold },
                        string.IsNullOrWhiteSpace(hint) ? new ContentView() : new Label { Text = hint },
                        _entry,
                        new HorizontalStackLayout
                        {
                            Spacing = 12,
                            Children = { confirmButton, cancelButton }
                        }
                    }
                }
            };
        }

        protected override void OnDisappearing()
        {
            _entry.Text = string.Empty;
            _completion.TrySetResult(null);
            base.OnDisappearing();
        }
    }
}