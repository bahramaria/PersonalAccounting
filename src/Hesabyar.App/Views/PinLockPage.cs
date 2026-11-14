using Hesabyar.App.Resources.Strings;
using Hesabyar.App.Services;
using Hesabyar.Localization;

namespace Hesabyar.App.Views;

public sealed class PinLockPage : ContentPage
{
    private readonly AppLockService _appLock;
    private readonly IServiceProvider _services;
    private readonly Entry _pinEntry;
    private readonly Label _statusLabel;

    public PinLockPage(AppLockService appLock, IServiceProvider services, ILocalizationService localization)
    {
        _appLock = appLock;
        _services = services;
        Title = AppStrings.PinUnlockTitle;
        FlowDirection = localization.FlowDirection == TextDirection.RightToLeft
            ? Microsoft.Maui.FlowDirection.RightToLeft
            : Microsoft.Maui.FlowDirection.LeftToRight;
        _pinEntry = new Entry
        {
            IsPassword = true, Keyboard = Keyboard.Numeric, MaxLength = 8,
            Placeholder = AppStrings.PinUnlockPrompt, HorizontalOptions = LayoutOptions.Fill
        };
        _statusLabel = new Label { HorizontalTextAlignment = TextAlignment.Center, TextColor = Colors.Red };
        var button = new Button { Text = AppStrings.PinUnlockButton, HorizontalOptions = LayoutOptions.Fill };
        button.Clicked += OnUnlockClicked;
        Content = new Grid
        {
            Padding = 28,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new VerticalStackLayout
                {
                    Spacing = 18,
                    Children =
                    {
                        new Label { Text = AppStrings.PinUnlockTitle, FontSize = 24, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center },
                        new Label { Text = AppStrings.PinUnlockDescription, HorizontalTextAlignment = TextAlignment.Center },
                        _pinEntry, _statusLabel, button
                    }
                }
            }
        };
    }

    private async void OnUnlockClicked(object? sender, EventArgs e)
    {
        var result = await _appLock.VerifyPinAsync(_pinEntry.Text);
        if (result.IsValid)
        {
            var window = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault();
            if (window is not null) window.Page = _services.GetRequiredService<AppShell>();
            return;
        }
        _pinEntry.Text = string.Empty;
        _statusLabel.Text = result.RetryAfter is { } retry
            ? string.Format(AppStrings.PinLockedForSeconds, Math.Ceiling(retry.TotalSeconds))
            : AppStrings.PinInvalid;
    }
}