using Hesabyar.App.Resources.Strings;
using Hesabyar.App.Services;
using Hesabyar.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace Hesabyar.App.Views;

public sealed class PinLockPage : ContentPage
{
    private readonly AppLockService _appLock;
    private readonly IServiceProvider _services;
    private readonly Entry _pinEntry;
    private readonly Label _statusLabel;
    private readonly IBiometricAuthenticator _biometric;
    private readonly Button _biometricButton;

    public PinLockPage(AppLockService appLock, IServiceProvider services, ILocalizationService localization, IBiometricAuthenticator biometric)
    {
        _appLock = appLock;
        _services = services;
        _biometric = biometric;
        _biometricButton = new Button { Text = AppStrings.BiometricUnlockButton, IsVisible = false };
        _biometricButton.Clicked += OnBiometricUnlockClicked;
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
                        _pinEntry, _statusLabel, button, _biometricButton
                    }
                }
            }
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _biometricButton.IsVisible = await _biometric.IsAvailableAsync();
    }

    private async void OnBiometricUnlockClicked(object? sender, EventArgs e)
    {
        try
        {
            if (await _biometric.AuthenticateAsync(AppStrings.BiometricUnlockTitle, AppStrings.BiometricUnlockReason, AppStrings.Cancel))
                OpenApplication();
            else
                _statusLabel.Text = AppStrings.BiometricUnlockFailed;
        }
        catch
        {
            _statusLabel.Text = AppStrings.BiometricUnlockFailed;
        }
    }

    private void OpenApplication()
    {
        var window = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault();
        if (window is not null) window.Page = _services.GetRequiredService<AppShell>();
    }

    private async void OnUnlockClicked(object? sender, EventArgs e)
    {
        var result = await _appLock.VerifyPinAsync(_pinEntry.Text);
        if (result.IsValid)
        {
            OpenApplication();
            return;
        }
        _pinEntry.Text = string.Empty;
        _statusLabel.Text = result.RetryAfter is { } retry
            ? string.Format(AppStrings.PinLockedForSeconds, Math.Ceiling(retry.TotalSeconds))
            : AppStrings.PinInvalid;
    }
}