using Hesabyar.App.Resources.Strings;
using Hesabyar.App.ViewModels;
using Hesabyar.Domain.Accounts;
using Hesabyar.Localization;

namespace Hesabyar.App.Views;

public partial class AccountsPage : ContentPage
{
    private readonly AccountsViewModel _viewModel;

    public AccountsPage(AccountsViewModel viewModel, ILocalizationService localization)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
        FlowDirection = localization.FlowDirection == TextDirection.RightToLeft
            ? Microsoft.Maui.FlowDirection.RightToLeft
            : Microsoft.Maui.FlowDirection.LeftToRight;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    private async void OnAddAccountClicked(object? sender, EventArgs e)
    {
        var name = await DisplayPromptAsync(
            AppStrings.AddAccount,
            AppStrings.AccountNamePrompt,
            AppStrings.Save,
            AppStrings.Cancel,
            maxLength: 120);

        if (string.IsNullOrWhiteSpace(name))
            return;

        var kindChoice = await DisplayActionSheet(
            AppStrings.AccountKindPrompt,
            AppStrings.Cancel,
            null,
            AppStrings.AccountKindCash,
            AppStrings.AccountKindBank,
            AppStrings.AccountKindCard,
            AppStrings.AccountKindWallet,
            AppStrings.AccountKindCreditCard);

        var kind = kindChoice switch
        {
            var value when value == AppStrings.AccountKindCash => AccountKind.Cash,
            var value when value == AppStrings.AccountKindBank => AccountKind.Bank,
            var value when value == AppStrings.AccountKindCard => AccountKind.Card,
            var value when value == AppStrings.AccountKindWallet => AccountKind.Wallet,
            var value when value == AppStrings.AccountKindCreditCard => AccountKind.CreditCard,
            _ => (AccountKind?)null
        };

        if (kind is null)
            return;

        var openingText = await DisplayPromptAsync(
            AppStrings.OpeningBalance,
            AppStrings.OpeningBalancePrompt,
            AppStrings.Save,
            AppStrings.Skip,
            initialValue: "0",
            keyboard: Keyboard.Numeric);

        var normalized = NormalizeDigits(openingText ?? "0")
            .Replace(",", string.Empty, StringComparison.Ordinal)
            .Replace("٬", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);

        if (!long.TryParse(normalized, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var openingBalance) ||
            openingBalance < 0)
        {
            await DisplayAlert(AppStrings.ValidationError, AppStrings.OpeningBalanceInvalid, AppStrings.Ok);
            return;
        }

        var created = await _viewModel.CreateAsync(name.Trim(), kind.Value, openingBalance);
        if (!created)
            await DisplayAlert(AppStrings.ValidationError, _viewModel.StatusMessage, AppStrings.Ok);
    }

    private static string NormalizeDigits(string value)
    {
        var result = new System.Text.StringBuilder(value.Length);
        foreach (var character in value)
        {
            var digit = character switch
            {
                >= '۰' and <= '۹' => character - '۰',
                >= '٠' and <= '٩' => character - '٠',
                _ => character
            };
            result.Append(digit);
        }

        return result.ToString();
    }
}
