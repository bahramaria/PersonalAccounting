using Hesabyar.App.Resources.Strings;
using Hesabyar.App.ViewModels;
using Hesabyar.Localization;

namespace Hesabyar.App.Views;

public partial class PeoplePage : ContentPage
{
    private readonly PeopleViewModel _viewModel;
    public PeoplePage(PeopleViewModel viewModel, ILocalizationService localization)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
        FlowDirection = localization.FlowDirection == TextDirection.RightToLeft
            ? Microsoft.Maui.FlowDirection.RightToLeft : Microsoft.Maui.FlowDirection.LeftToRight;
    }

    protected override async void OnAppearing() { base.OnAppearing(); await _viewModel.LoadAsync(); }

    private async void OnAddPersonClicked(object? sender, EventArgs e)
    {
        var name = await DisplayPromptAsync(AppStrings.PersonAdd, AppStrings.PersonName, AppStrings.Save, AppStrings.Cancel, maxLength: 120);
        if (string.IsNullOrWhiteSpace(name)) return;
        var phone = await DisplayPromptAsync(AppStrings.PersonPhone, AppStrings.PersonPhone, AppStrings.Save, AppStrings.Skip, maxLength: 40);
        if (!await _viewModel.CreateAsync(name, phone, null))
            await DisplayAlertAsync(AppStrings.ValidationError, _viewModel.StatusMessage, AppStrings.Ok);
    }

    private async void OnPersonSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not PersonListItem person) return;
        if (sender is CollectionView list) list.SelectedItem = null;
        var action = await DisplayActionSheetAsync(person.Name, AppStrings.Cancel, null,
            AppStrings.PersonLend, AppStrings.PersonBorrow, AppStrings.PersonRepay, AppStrings.PersonHistory,
            AppStrings.PersonShare, AppStrings.PersonEdit, AppStrings.PersonArchive);
        if (action == AppStrings.PersonHistory)
        {
            var history = await _viewModel.GetHistoryTextAsync(person);
            await DisplayAlertAsync(AppStrings.PersonHistory, history, AppStrings.Ok);
            return;
        }
        if (action == AppStrings.PersonShare)
        {
            await _viewModel.ShareSummaryAsync(person);
            return;
        }
        if (action == AppStrings.PersonEdit)
        {
            var name = await DisplayPromptAsync(AppStrings.PersonEdit, AppStrings.PersonName, AppStrings.Save, AppStrings.Cancel, initialValue: person.Name);
            if (string.IsNullOrWhiteSpace(name)) return;
            var phone = await DisplayPromptAsync(AppStrings.PersonPhone, AppStrings.PersonPhone, AppStrings.Save, AppStrings.Cancel, initialValue: person.Phone);
            if (!await _viewModel.UpdateAsync(person.Id, name, phone, null))
                await DisplayAlertAsync(AppStrings.ValidationError, _viewModel.StatusMessage, AppStrings.Ok);
            return;
        }
        if (action == AppStrings.PersonArchive)
        {
            var ok = await DisplayAlertAsync(AppStrings.PersonArchive, AppStrings.PersonArchiveConfirm, AppStrings.PersonArchive, AppStrings.Cancel);
            if (ok && !await _viewModel.ArchiveAsync(person.Id))
                await DisplayAlertAsync(AppStrings.ValidationError, _viewModel.StatusMessage, AppStrings.Ok);
            return;
        }
        if (action != AppStrings.PersonLend && action != AppStrings.PersonBorrow && action != AppStrings.PersonRepay) return;
        if (_viewModel.Accounts.Count == 0)
        {
            await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.PersonNoAccount, AppStrings.Ok);
            return;
        }
        var selectedName = await DisplayActionSheetAsync(AppStrings.TransactionAccountPrompt, AppStrings.Cancel, null,
            _viewModel.Accounts.Select(x => x.Name).ToArray());
        var account = _viewModel.Accounts.FirstOrDefault(x => x.Name == selectedName);
        if (account is null) return;
        var amountText = await DisplayPromptAsync(AppStrings.TransactionAmountPrompt, AppStrings.TransactionAmountPrompt,
            AppStrings.Save, AppStrings.Cancel, keyboard: Keyboard.Numeric);
        if (!TryParseAmount(amountText, out var amount))
        {
            await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.TransactionAmountInvalid, AppStrings.Ok);
            return;
        }
        var note = await DisplayPromptAsync(AppStrings.TransactionDescriptionPrompt, AppStrings.TransactionDescriptionPrompt,
            AppStrings.Save, AppStrings.Skip, maxLength: 500);
        bool saved;
        if (action == AppStrings.PersonRepay && person.BalanceMinor != 0 &&
            person.BalanceMinor != long.MinValue && amount > Math.Abs(person.BalanceMinor))
        {
            var convertExcess = await DisplayAlertAsync(AppStrings.PersonRepay, AppStrings.PersonExcessRepaymentPrompt,
                AppStrings.Continue, AppStrings.Cancel);
            if (!convertExcess) return;
            var outstanding = Math.Abs(person.BalanceMinor);
            saved = await _viewModel.RepayAsync(person.Id, account.Id, outstanding, note);
            if (saved)
                saved = await _viewModel.AddDebtAsync(person.Id, account.Id, amount - outstanding,
                    person.BalanceMinor < 0, AppStrings.PersonExcessRepaymentNote);
        }
        else
        {
            saved = action == AppStrings.PersonRepay
                ? await _viewModel.RepayAsync(person.Id, account.Id, amount, note)
                : await _viewModel.AddDebtAsync(person.Id, account.Id, amount, action == AppStrings.PersonLend, note);
        }
        if (!saved) await DisplayAlertAsync(AppStrings.ValidationError, _viewModel.StatusMessage, AppStrings.Ok);
    }

    private static bool TryParseAmount(string? value, out long amount)
    {
        amount = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var normalized = value.Replace("۰", "0").Replace("۱", "1").Replace("۲", "2").Replace("۳", "3")
            .Replace("۴", "4").Replace("۵", "5").Replace("۶", "6").Replace("۷", "7").Replace("۸", "8").Replace("۹", "9")
            .Replace("٬", "").Replace(",", "").Replace(" ", "");
        return long.TryParse(normalized, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out amount) && amount > 0;
    }
}
