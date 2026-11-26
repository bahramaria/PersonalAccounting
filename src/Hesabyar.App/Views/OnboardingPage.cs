using Hesabyar.Application.Accounts;
using Hesabyar.App.Resources.Strings;
using Hesabyar.App.Services;
using Hesabyar.Domain.Accounts;
using Hesabyar.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace Hesabyar.App.Views;

public sealed class OnboardingPage : ContentPage
{
    private readonly IServiceProvider _services;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AppLockService _appLock;
    private readonly TimeProvider _timeProvider;
    private readonly Entry _accountNameEntry;
    private readonly Picker _accountKindPicker;
    private readonly Entry _openingBalanceEntry;
    private readonly CheckBox _enablePinCheckBox;
    private readonly Button _createAccountButton;

    public OnboardingPage(
        IServiceProvider services,
        IServiceScopeFactory scopeFactory,
        ILocalizationService localization,
        AppLockService appLock,
        TimeProvider timeProvider)
    {
        _services = services;
        _scopeFactory = scopeFactory;
        _appLock = appLock;
        _timeProvider = timeProvider;

        FlowDirection = localization.FlowDirection == TextDirection.RightToLeft
            ? Microsoft.Maui.FlowDirection.RightToLeft
            : Microsoft.Maui.FlowDirection.LeftToRight;

        _accountNameEntry = new Entry
        {
            Text = AppStrings.OnboardingDefaultAccountName,
            Placeholder = AppStrings.OnboardingAccountName,
            MaxLength = 120,
            ClearButtonVisibility = ClearButtonVisibility.WhileEditing,
            ReturnType = ReturnType.Next
        };
        _accountKindPicker = new Picker
        {
            Title = AppStrings.OnboardingAccountType,
            Items =
            {
                AppStrings.AccountKindCash,
                AppStrings.AccountKindBank,
                AppStrings.AccountKindCard,
                AppStrings.AccountKindWallet,
                AppStrings.AccountKindCreditCard
            },
            SelectedIndex = 0
        };
        _openingBalanceEntry = new Entry
        {
            Placeholder = AppStrings.OnboardingOpeningBalance,
            Keyboard = Keyboard.Numeric,
            Text = "0"
        };
        _enablePinCheckBox = new CheckBox { IsChecked = false };
        _createAccountButton = new Button { Text = AppStrings.OnboardingCreateAccount };
        _createAccountButton.Clicked += OnCreateAccountClicked;

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(24, 32),
                Spacing = 18,
                Children =
                {
                    new Label
                    {
                        Text = AppStrings.OnboardingTitle,
                        FontSize = 28,
                        FontAttributes = FontAttributes.Bold
                    },
                    new Label
                    {
                        Text = AppStrings.OnboardingDescription,
                        FontSize = 15,
                        Opacity = 0.8
                    },
                    new Label { Text = AppStrings.OnboardingAccountName, FontAttributes = FontAttributes.Bold },
                    _accountNameEntry,
                    new Label { Text = AppStrings.OnboardingAccountType, FontAttributes = FontAttributes.Bold },
                    _accountKindPicker,
                    new Label { Text = AppStrings.OnboardingOpeningBalance, FontAttributes = FontAttributes.Bold },
                    _openingBalanceEntry,
                    new HorizontalStackLayout
                    {
                        Spacing = 10,
                        VerticalOptions = LayoutOptions.Center,
                        Children =
                        {
                            _enablePinCheckBox,
                            new Label
                            {
                                Text = AppStrings.OnboardingEnablePin,
                                VerticalOptions = LayoutOptions.Center
                            }
                        }
                    },
                    new Label
                    {
                        Text = AppStrings.OnboardingSmsNote,
                        FontSize = 13,
                        Opacity = 0.75
                    },
                    _createAccountButton
                }
            }
        };
    }

    private async void OnCreateAccountClicked(object? sender, EventArgs e)
    {
        if (!_createAccountButton.IsEnabled)
            return;

        var accountName = _accountNameEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(accountName))
        {
            await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.OnboardingAccountName, AppStrings.Ok);
            return;
        }

        var openingBalanceText = string.IsNullOrWhiteSpace(_openingBalanceEntry.Text)
            ? "0"
            : _openingBalanceEntry.Text;
        if (!MoneyDisplayService.TryParse(openingBalanceText, "IRR", out var openingBalance, allowZero: true))
        {
            await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.OnboardingInvalidAmount, AppStrings.Ok);
            return;
        }

        if (_accountKindPicker.SelectedIndex is < 0 or > 4)
        {
            await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.OnboardingAccountType, AppStrings.Ok);
            return;
        }

        _createAccountButton.IsEnabled = false;
        var accountCreated = false;
        try
        {
            var kind = _accountKindPicker.SelectedIndex switch
            {
                0 => AccountKind.Cash,
                1 => AccountKind.Bank,
                2 => AccountKind.Card,
                3 => AccountKind.Wallet,
                _ => AccountKind.CreditCard
            };
            var type = kind == AccountKind.CreditCard ? AccountType.Liability : AccountType.Asset;

            using var scope = _scopeFactory.CreateScope();
            var accountService = scope.ServiceProvider.GetRequiredService<AccountService>();
            var result = await accountService.CreateAsync(new CreateAccountRequest(
                accountName,
                type,
                kind,
                CurrencyCode: "IRR",
                OpeningBalanceMinor: openingBalance,
                OpeningDate: openingBalance > 0
                    ? DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime)
                    : null));

            if (!result.IsSuccess)
            {
                await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.OnboardingAccountFailed, AppStrings.Ok);
                return;
            }
            accountCreated = true;

            if (_enablePinCheckBox.IsChecked)
            {
                try
                {
                    await ConfigureOptionalPinAsync();
                }
                catch (Exception)
                {
                    // PIN setup is optional; account creation must still complete.
                }
            }

            var createFirstTransaction = await DisplayAlertAsync(
                AppStrings.OnboardingFirstTransaction,
                AppStrings.OnboardingFirstTransactionPrompt,
                AppStrings.Continue,
                AppStrings.Skip);

            var shell = _services.GetRequiredService<AppShell>();
            var window = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault();
            if (window is null)
                throw new InvalidOperationException("The application window is not available.");

            window.Page = shell;
            if (createFirstTransaction)
                await shell.GoToAsync("//transactions");
        }
        catch (Exception)
        {
            await DisplayAlertAsync(
                AppStrings.OnboardingTitle,
                accountCreated ? AppStrings.StartupFailed : AppStrings.OnboardingAccountFailed,
                AppStrings.Ok);
        }
        finally
        {
            _createAccountButton.IsEnabled = !accountCreated;
        }
    }

    private async Task ConfigureOptionalPinAsync()
    {
        var pin = await PromptPinAsync(AppStrings.PinEnterNew);
        if (string.IsNullOrEmpty(pin))
            return;

        if (pin.Length is < 4 or > 8 || pin.Any(character => character is < '0' or > '9'))
        {
            await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.OnboardingPinInvalid, AppStrings.Ok);
            return;
        }

        var confirmation = await PromptPinAsync(AppStrings.PinConfirm);
        if (confirmation is null)
            return;

        if (!string.Equals(pin, confirmation, StringComparison.Ordinal))
        {
            await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.PinMismatch, AppStrings.Ok);
            return;
        }

        await _appLock.SetPinAsync(pin);
    }

    private async Task<string?> PromptPinAsync(string prompt)
    {
        var promptPage = new PinPromptPage(prompt, AppStrings.Continue, AppStrings.Cancel);
        await Navigation.PushModalAsync(promptPage);
        return await promptPage.Result;
    }

    private sealed class PinPromptPage : ContentPage
    {
        private readonly Entry _entry;
        private readonly TaskCompletionSource<string?> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<string?> Result => _completion.Task;

        public PinPromptPage(string prompt, string confirmText, string cancelText)
        {
            _entry = new Entry
            {
                IsPassword = true,
                MaxLength = 8,
                Keyboard = Keyboard.Numeric,
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
