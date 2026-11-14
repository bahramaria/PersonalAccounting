using Hesabyar.Application.Security;
using Hesabyar.App.Resources.Strings;
using Hesabyar.App.Services;
using Hesabyar.Localization;
using Hesabyar.Localization.Calendar;
using Microsoft.Maui.Storage;

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

    public SettingsPage(IServiceProvider services, ILocalizationService localization,
        IDateCalendarService calendar, AppLockService appLock)
    {
        _services = services;
        _calendar = calendar;
        _appLock = appLock;
        Title = AppStrings.SettingsHeading;
        FlowDirection = localization.FlowDirection == TextDirection.RightToLeft
            ? Microsoft.Maui.FlowDirection.RightToLeft : Microsoft.Maui.FlowDirection.LeftToRight;
        _languageButton.Clicked += OnLanguageClicked;
        _themeButton.Clicked += OnThemeClicked;
        _calendarButton.Clicked += OnCalendarClicked;
        _pinButton.Clicked += OnPinClicked;
        _autoLockButton.Clicked += OnAutoLockClicked;
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 24, Spacing = 16,
                Children =
                {
                    new Label { Text = AppStrings.SettingsHeading, FontSize = 24, FontAttributes = FontAttributes.Bold },
                    _languageButton, _themeButton, _calendarButton, _pinButton, _autoLockButton
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
        DisplayPromptAsync(AppStrings.PinSettings, prompt, AppStrings.Continue, AppStrings.Cancel,
            maxLength: 8, keyboard: Keyboard.Numeric);

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
        _autoLockButton.Text = $"{AppStrings.PinAutoLock}: {Preferences.Get("security.auto-lock-seconds", 0) switch
        {
            30 => AppStrings.PinAutoLock30Seconds,
            60 => AppStrings.PinAutoLock1Minute,
            300 => AppStrings.PinAutoLock5Minutes,
            _ => AppStrings.PinAutoLockImmediate
        }}";
    }
}