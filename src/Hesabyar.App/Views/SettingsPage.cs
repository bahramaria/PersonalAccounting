using Microsoft.Extensions.DependencyInjection;
using Hesabyar.App.Resources.Strings;
using Hesabyar.App.Services;
using Hesabyar.Localization;
using Hesabyar.Localization.Calendar;
using Microsoft.Maui.Storage;

namespace Hesabyar.App.Views;

public sealed class SettingsPage : ContentPage
{
    private readonly IServiceProvider _services;
    private readonly Button _languageButton;
    private readonly Button _themeButton;
    private readonly Button _calendarButton;
    private readonly IDateCalendarService _calendar;

    public SettingsPage(IServiceProvider services, ILocalizationService localization, IDateCalendarService calendar)
    {
        _services = services;
        _calendar = calendar;
        Title = AppStrings.SettingsHeading;
        FlowDirection = localization.FlowDirection == TextDirection.RightToLeft
            ? Microsoft.Maui.FlowDirection.RightToLeft
            : Microsoft.Maui.FlowDirection.LeftToRight;
        _languageButton = new Button { HorizontalOptions = LayoutOptions.Fill };
        _themeButton = new Button { HorizontalOptions = LayoutOptions.Fill };
        _calendarButton = new Button { HorizontalOptions = LayoutOptions.Fill };
        _languageButton.Clicked += OnLanguageClicked;
        _themeButton.Clicked += OnThemeClicked;
        _calendarButton.Clicked += OnCalendarClicked;
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 24,
                Spacing = 16,
                Children =
                {
                    new Label { Text = AppStrings.SettingsHeading, FontSize = 24, FontAttributes = FontAttributes.Bold },
                    _languageButton,
                    _themeButton,
                    _calendarButton
                }
            }
        };
        RefreshLabels();
    }

    private async void OnLanguageClicked(object? sender, EventArgs e)
    {
        var selected = await DisplayActionSheetAsync(AppStrings.SettingsLanguage, AppStrings.Cancel, null,
            AppStrings.SettingsLanguagePersian, AppStrings.SettingsLanguageEnglish);
        if (selected == AppStrings.SettingsLanguagePersian)
            Preferences.Set("app.language", "fa-IR");
        else if (selected == AppStrings.SettingsLanguageEnglish)
            Preferences.Set("app.language", "en-US");
        else
            return;
        AppLocalization.Initialize();
        var window = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault();
        if (window is not null)
            window.Page = _services.GetRequiredService<AppShell>();
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
        {
            app.UserAppTheme = theme switch
            {
                "light" => AppTheme.Light,
                "dark" => AppTheme.Dark,
                _ => AppTheme.Unspecified
            };
        }
        RefreshLabels();
    }

    private async void OnCalendarClicked(object? sender, EventArgs e)
    {
        var isPersian = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fa";
        var persianLabel = isPersian ? "شمسی" : "Persian (Jalali)";
        var gregorianLabel = isPersian ? "میلادی" : "Gregorian";
        var selected = await DisplayActionSheetAsync(isPersian ? "تقویم" : "Calendar",
            AppStrings.Cancel, null, persianLabel, gregorianLabel);
        if (selected == persianLabel)
            Preferences.Set("app.calendar", "persian");
        else if (selected == gregorianLabel)
            Preferences.Set("app.calendar", "gregorian");
        else
            return;

        _calendar.CurrentCalendar = Preferences.Get("app.calendar", "persian") == "gregorian"
            ? CalendarKind.Gregorian : CalendarKind.Persian;
        RefreshLabels();
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
        var isPersian = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fa";
        var calendar = _calendar.CurrentCalendar == CalendarKind.Gregorian
            ? (isPersian ? "میلادی" : "Gregorian")
            : (isPersian ? "شمسی" : "Persian (Jalali)");
        _calendarButton.Text = $"{(isPersian ? "تقویم" : "Calendar")}: {calendar}";
    }
}
