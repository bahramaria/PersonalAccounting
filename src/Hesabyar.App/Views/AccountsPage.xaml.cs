using Hesabyar.App.Resources.Strings;
using Hesabyar.App.ViewModels;
using Hesabyar.App.Services;
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

    private async void OnToggleArchivedClicked(object? sender, EventArgs e) =>
        await _viewModel.ToggleArchivedAsync();

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

        var kindChoice = await DisplayActionSheetAsync(
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

        var currencyCode = await DisplayPromptAsync(AppStrings.CurrencyCodePrompt, AppStrings.CurrencyCodePrompt,
            AppStrings.Save, AppStrings.Skip, initialValue: "IRR", maxLength: 3);
        if (currencyCode is null) return;
        currencyCode = string.IsNullOrWhiteSpace(currencyCode) ? "IRR" : currencyCode.Trim().ToUpperInvariant();

        var openingText = await DisplayPromptAsync(
            AppStrings.OpeningBalance,
            $"{AppStrings.OpeningBalancePrompt} ({currencyCode})",
            AppStrings.Save,
            AppStrings.Skip,
            initialValue: "0",
            keyboard: Keyboard.Numeric);

        if (!MoneyDisplayService.TryParse(openingText ?? "0", currencyCode, out var openingBalance, allowZero: true))
        {
            await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.OpeningBalanceInvalid, AppStrings.Ok);
            return;
        }

        var created = await _viewModel.CreateAsync(name.Trim(), kind.Value, openingBalance, currencyCode);
        if (!created)
            await DisplayAlertAsync(AppStrings.ValidationError, _viewModel.StatusMessage, AppStrings.Ok);
    }

    private async void OnAccountSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not AccountListItem account)
            return;

        if (sender is CollectionView collectionView)
            collectionView.SelectedItem = null;

        var action = await DisplayActionSheetAsync(
            account.Name,
            AppStrings.Cancel,
            null,
            account.IsArchived
                ? [AppStrings.RestoreAccount]
                : [AppStrings.EditAccount, AppStrings.ArchiveAccount]);

        if (action == AppStrings.RestoreAccount)
        {
            if (!await _viewModel.RestoreAsync(account.Id))
                await DisplayAlertAsync(AppStrings.ValidationError, _viewModel.StatusMessage, AppStrings.Ok);
        }
        else if (!account.IsArchived && action == AppStrings.EditAccount)
        {
            var name = await DisplayPromptAsync(
                AppStrings.EditAccount,
                AppStrings.AccountNamePrompt,
                AppStrings.Save,
                AppStrings.Cancel,
                initialValue: account.Name,
                maxLength: 120);

            if (string.IsNullOrWhiteSpace(name) || string.Equals(name.Trim(), account.Name, StringComparison.Ordinal))
                return;

            if (!await _viewModel.RenameAsync(account.Id, name.Trim()))
                await DisplayAlertAsync(AppStrings.ValidationError, _viewModel.StatusMessage, AppStrings.Ok);
        }
        else if (!account.IsArchived && action == AppStrings.ArchiveAccount)
        {
            var confirmed = await DisplayAlertAsync(
                AppStrings.ArchiveAccount,
                AppStrings.ArchiveAccountConfirmation,
                AppStrings.ArchiveAccount,
                AppStrings.Cancel);

            if (confirmed && !await _viewModel.ArchiveAsync(account.Id))
                await DisplayAlertAsync(AppStrings.ValidationError, _viewModel.StatusMessage, AppStrings.Ok);
        }
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
