using Hesabyar.Application.Accounts;
using Hesabyar.Application.Security;
using Hesabyar.App.Resources.Strings;
using Hesabyar.App.Services;
using Hesabyar.App.Views;
using Hesabyar.Domain.Accounts;
using Hesabyar.Localization;
using Hesabyar.Localization.Numbers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;

namespace Hesabyar.App;

public sealed class OnboardingPage : ContentPage
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IServiceProvider _services;
    private readonly AppLockService _appLock;
    private readonly INumberNormalizer _numberNormalizer;
    private readonly Entry _accountName = new() { Text = AppStrings.OnboardingDefaultAccountName, MaxLength = 120 };
    private readonly Picker _accountType = new() { Title = AppStrings.OnboardingAccountType };
    private readonly Entry _openingBalance = new() { Keyboard = Keyboard.Numeric, Placeholder = "0" };
    private readonly Switch _enablePin = new();
    private readonly Label _status = new() { TextColor = Colors.Red, FontSize = 13 };
    private readonly Button _createButton = new() { Text = AppStrings.OnboardingCreateAccount };
    private bool _isSaving;

    public OnboardingPage(
        IServiceScopeFactory scopeFactory,
        IServiceProvider services,
        AppLockService appLock,
        INumberNormalizer numberNormalizer,
        ILocalizationService localization)
    {
        _scopeFactory = scopeFactory;
        _services = services;
        _appLock = appLock;
        _numberNormalizer = numberNormalizer;
        Title = AppStrings.OnboardingTitle;
        FlowDirection = localization.FlowDirection == TextDirection.RightToLeft
            ? Microsoft.Maui.FlowDirection.RightToLeft
            : Microsoft.Maui.FlowDirection.LeftToRight;

        _accountType.ItemsSource =
        [
            AppStrings.AccountKindCash,
            AppStrings.AccountKindBank,
            AppStrings.AccountKindCard,
            AppStrings.AccountKindWallet
        ];
        _accountType.SelectedIndex = 0;
        _createButton.Clicked += OnCreateAccountClicked;

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 24,
                Spacing = 16,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new Label { Text = AppStrings.OnboardingTitle, FontSize = 26, FontAttributes = FontAttributes.Bold },
                    new Label { Text = AppStrings.OnboardingDescription, FontSize = 14 },
                    new Label { Text = AppStrings.OnboardingAccountName, FontAttributes = FontAttributes.Bold },
                    _accountName,
                    new Label { Text = AppStrings.OnboardingAccountType, FontAttributes = FontAttributes.Bold },
                    _accountType,
                    new Label { Text = AppStrings.OnboardingOpeningBalance, FontAttributes = FontAttributes.Bold },
                    _openingBalance,
                    new HorizontalStackLayout
                    {
                        Spacing = 10,
                        Children =
                        {
                            _enablePin,
                            new Label { Text = AppStrings.OnboardingEnablePin, VerticalOptions = LayoutOptions.Center }
                        }
                    },
                    new Label { Text = AppStrings.OnboardingSmsNote, FontSize = 12, Opacity = 0.75 },
                    _status,
                    _createButton
                }
            }
        };
    }

    private async void OnCreateAccountClicked(object? sender, EventArgs e)
    {
        if (_isSaving)
            return;

        var name = _accountName.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            _status.Text = AppStrings.AccountNamePrompt;
            return;
        }

        var rawAmount = string.IsNullOrWhiteSpace(_openingBalance.Text) ? "0" : _openingBalance.Text;
        if (!_numberNormalizer.TryParseAmount(rawAmount, out var parsedAmount) ||
            parsedAmount < 0 || parsedAmount > long.MaxValue || decimal.Truncate(parsedAmount) != parsedAmount)
        {
            _status.Text = AppStrings.OnboardingInvalidAmount;
            return;
        }

        var kind = _accountType.SelectedIndex switch
        {
            0 => AccountKind.Cash,
            1 => AccountKind.Bank,
            2 => AccountKind.Card,
            3 => AccountKind.Wallet,
            _ => (AccountKind?)null
        };
        if (kind is null)
        {
            _status.Text = AppStrings.AccountKindPrompt;
            return;
        }

        _isSaving = true;
        _createButton.IsEnabled = false;
        _status.Text = string.Empty;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<AccountService>();
            var amount = (long)parsedAmount;
            var result = await service.CreateAsync(new CreateAccountRequest(
                name,
                AccountType.Asset,
                kind.Value,
                CurrencyCode: "IRR",
                OpeningBalanceMinor: amount,
                OpeningDate: amount > 0 ? DateOnly.FromDateTime(DateTime.Today) : null));

            if (!result.IsSuccess)
            {
                _status.Text = result.Error.Message ?? AppStrings.OnboardingAccountFailed;
                return;
            }

            if (_enablePin.IsToggled)
                await TryEnablePinAsync();

            Preferences.Set("onboarding.completed", true);
            var startTransaction = await DisplayAlertAsync(
                AppStrings.OnboardingFirstTransaction,
                AppStrings.OnboardingFirstTransactionPrompt,
                AppStrings.Continue,
                AppStrings.Skip);

            var window = Application.Current?.Windows.FirstOrDefault();
            if (window is null)
                throw new InvalidOperationException("The application window is not available.");
            var shell = _services.GetRequiredService<AppShell>();
            window.Page = shell;
            if (startTransaction)
            {
                await shell.GoToAsync("//transactions");
                if (shell.CurrentPage is TransactionsPage page)
                    await page.OpenQuickAddAsync();
            }
        }
        catch (Exception)
        {
            _status.Text = AppStrings.OnboardingAccountFailed;
        }
        finally
        {
            _isSaving = false;
            _createButton.IsEnabled = true;
        }
    }

    private async Task TryEnablePinAsync()
    {
        var prompt = new PinSetupPromptPage();
        await Navigation.PushModalAsync(prompt);
        var credentials = await prompt.Result;
        if (credentials is null)
            return;

        if (!PinHasher.IsValidPin(credentials.Value.Pin) ||
            PinHasher.NormalizePin(credentials.Value.Pin) != PinHasher.NormalizePin(credentials.Value.Confirmation))
        {
            await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.OnboardingPinInvalid, AppStrings.Ok);
            return;
        }

        await _appLock.SetPinAsync(credentials.Value.Pin);
    }

    private sealed class PinSetupPromptPage : ContentPage
    {
        private readonly Entry _pin = new()
        {
            IsPassword = true,
            MaxLength = 8,
            Keyboard = Keyboard.Numeric
        };
        private readonly Entry _confirmation = new()
        {
            IsPassword = true,
            MaxLength = 8,
            Keyboard = Keyboard.Numeric
        };
        private readonly TaskCompletionSource<(string Pin, string Confirmation)?> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<(string Pin, string Confirmation)?> Result => _completion.Task;

        public PinSetupPromptPage()
        {
            Title = AppStrings.PinSettings;
            var saveButton = new Button { Text = AppStrings.Continue };
            var cancelButton = new Button { Text = AppStrings.Cancel };
            saveButton.Clicked += async (_, _) =>
            {
                _completion.TrySetResult((_pin.Text ?? string.Empty, _confirmation.Text ?? string.Empty));
                await Navigation.PopModalAsync();
            };
            cancelButton.Clicked += async (_, _) =>
            {
                _completion.TrySetResult(null);
                await Navigation.PopModalAsync();
            };

            Content = new VerticalStackLayout
            {
                Padding = 24,
                Spacing = 16,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new Label { Text = AppStrings.PinEnterNew },
                    _pin,
                    new Label { Text = AppStrings.PinConfirm },
                    _confirmation,
                    new HorizontalStackLayout
                    {
                        Spacing = 12,
                        Children = { saveButton, cancelButton }
                    }
                }
            };
        }

        protected override void OnDisappearing()
        {
            _pin.Text = string.Empty;
            _confirmation.Text = string.Empty;
            _completion.TrySetResult(null);
            base.OnDisappearing();
        }
    }
}
