using Hesabyar.App.Services;
using Hesabyar.Application.Cheques;
using Hesabyar.App.Resources.Strings;
using Hesabyar.App.ViewModels;
using Hesabyar.Localization;
using Hesabyar.Localization.Calendar;
using Hesabyar.Localization.Numbers;

namespace Hesabyar.App.Views;

public partial class ChequesPage : ContentPage
{
    private readonly ChequesViewModel _viewModel;
    private readonly IDateCalendarService _calendar;
    private readonly INumberNormalizer _numbers;

    public ChequesPage(ChequesViewModel viewModel, ILocalizationService localization,
        IDateCalendarService calendar, INumberNormalizer numbers)
    {
        InitializeComponent();
        _viewModel = viewModel; _calendar = calendar; _numbers = numbers;
        BindingContext = viewModel;
        FlowDirection = localization.FlowDirection == TextDirection.RightToLeft
            ? Microsoft.Maui.FlowDirection.RightToLeft : Microsoft.Maui.FlowDirection.LeftToRight;
    }

    protected override async void OnAppearing() { base.OnAppearing(); await _viewModel.LoadAsync(); }

    private async void OnAddClicked(object? sender, EventArgs e)
    {
        var directionText = await DisplayActionSheetAsync(AppStrings.ChequeAdd, AppStrings.Cancel, null,
            AppStrings.ChequeReceived, AppStrings.ChequeIssued);
        if (directionText != AppStrings.ChequeReceived && directionText != AppStrings.ChequeIssued) return;
        var currencyCode = await DisplayActionSheetAsync(AppStrings.ChequeCurrency, AppStrings.Cancel, null,
            _viewModel.CurrencyCodes.ToArray());
        if (string.IsNullOrWhiteSpace(currencyCode) || currencyCode == AppStrings.Cancel) return;
        var bank = await DisplayPromptAsync(AppStrings.ChequeBank, AppStrings.ChequeBank, AppStrings.Save, AppStrings.Cancel, maxLength: 80);
        if (string.IsNullOrWhiteSpace(bank)) return;
        var serial = await DisplayPromptAsync(AppStrings.ChequeSerial, AppStrings.ChequeSerial, AppStrings.Save, AppStrings.Cancel, maxLength: 40);
        if (string.IsNullOrWhiteSpace(serial)) return;
        var sayad = await DisplayPromptAsync(AppStrings.ChequeSayad, AppStrings.ChequeSayad, AppStrings.Save, AppStrings.Skip, maxLength: 24);
        var dueText = await DisplayPromptAsync(AppStrings.ChequeDueDate, AppStrings.ChequeDueDate, AppStrings.Save, AppStrings.Cancel);
        if (!_calendar.TryParse(dueText, out var dueDate))
        { await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.ChequeInvalid, AppStrings.Ok); return; }
        var amountText = await DisplayPromptAsync(AppStrings.ChequeAmount,
            $"{AppStrings.ChequeAmount} ({currencyCode})", AppStrings.Save, AppStrings.Cancel, keyboard: Keyboard.Numeric);
        if (!MoneyDisplayService.TryParse(amountText, currencyCode, out var amount))
        { await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.ChequeInvalid, AppStrings.Ok); return; }
        var personText = await DisplayActionSheetAsync(AppStrings.ChequePerson, AppStrings.Skip, null,
            _viewModel.People.Select(x => x.Name).ToArray());
        var person = _viewModel.People.FirstOrDefault(x => x.Name == personText);
        var note = await DisplayPromptAsync(AppStrings.ChequeNote, AppStrings.ChequeNote, AppStrings.Save, AppStrings.Skip, maxLength: 500);
        var request = new CreateChequeRequest(directionText == AppStrings.ChequeReceived ? ChequeDirection.Received : ChequeDirection.Issued,
            bank, serial, sayad, dueDate, person?.Id, amount, note, currencyCode);
        if (!await _viewModel.CreateAsync(request))
            await DisplayAlertAsync(AppStrings.ValidationError, _viewModel.StatusMessage, AppStrings.Ok);
    }

    private async void OnChequeSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not ChequeListItem cheque) return;
        if (sender is CollectionView list) list.SelectedItem = null;
        var actions = new List<string> { AppStrings.ChequeEdit, AppStrings.ChequeChangeStatus, AppStrings.ChequeDelete };
        var action = await DisplayActionSheetAsync($"{cheque.BankName} · {cheque.SerialNumber} · {cheque.CurrencyCode}", AppStrings.Cancel, null, actions.ToArray());
        if (action == AppStrings.ChequeEdit)
        {
            var bank = await DisplayPromptAsync(AppStrings.ChequeBank, AppStrings.ChequeBank, AppStrings.Save, AppStrings.Cancel, initialValue: cheque.BankName);
            if (string.IsNullOrWhiteSpace(bank)) return;
            var serial = await DisplayPromptAsync(AppStrings.ChequeSerial, AppStrings.ChequeSerial, AppStrings.Save, AppStrings.Cancel, initialValue: cheque.SerialNumber);
            if (string.IsNullOrWhiteSpace(serial)) return;
            var sayad = await DisplayPromptAsync(AppStrings.ChequeSayad, AppStrings.ChequeSayad, AppStrings.Save, AppStrings.Skip, initialValue: cheque.SayadNumber);
            var dueText = await DisplayPromptAsync(AppStrings.ChequeDueDate, AppStrings.ChequeDueDate, AppStrings.Save, AppStrings.Cancel, initialValue: cheque.DueDateText);
            if (!_calendar.TryParse(dueText, out var dueDate)) return;
            var amountText = await DisplayPromptAsync(AppStrings.ChequeAmount, $"{AppStrings.ChequeAmount} ({cheque.CurrencyCode})",
                AppStrings.Save, AppStrings.Cancel, initialValue: MoneyDisplayService.FormatInput(cheque.AmountMinor, cheque.CurrencyCode), keyboard: Keyboard.Numeric);
            if (!TryParseAmount(amountText, out var amount)) return;
            var personText = await DisplayActionSheetAsync(AppStrings.ChequePerson, AppStrings.Skip, null, _viewModel.People.Select(x => x.Name).ToArray());
            var person = _viewModel.People.FirstOrDefault(x => x.Name == personText);
            var note = await DisplayPromptAsync(AppStrings.ChequeNote, AppStrings.ChequeNote, AppStrings.Save, AppStrings.Skip, initialValue: cheque.Note);
            if (!await _viewModel.UpdateAsync(cheque.Id, new UpdateChequeRequest(bank, serial, sayad, dueDate, person?.Id, amount, note, cheque.CurrencyCode)))
                await DisplayAlertAsync(AppStrings.ValidationError, _viewModel.StatusMessage, AppStrings.Ok);
            return;
        }
        if (action == AppStrings.ChequeDelete)
        {
            if (await DisplayAlertAsync(AppStrings.ChequeDelete, AppStrings.ChequeDeleteConfirm, AppStrings.ChequeDelete, AppStrings.Cancel) &&
                !await _viewModel.DeleteAsync(cheque.Id))
                await DisplayAlertAsync(AppStrings.ValidationError, _viewModel.StatusMessage, AppStrings.Ok);
            return;
        }
        if (action != AppStrings.ChequeChangeStatus) return;
        var statuses = cheque.Status switch
        {
            ChequeStatus.Pending => new[] { AppStrings.ChequeCleared, AppStrings.ChequeReturned, AppStrings.ChequeSpent, AppStrings.ChequeVoided },
            ChequeStatus.Returned => new[] { AppStrings.ChequeCleared, AppStrings.ChequeVoided },
            ChequeStatus.Cleared => new[] { AppStrings.ChequeReturned },
            _ => Array.Empty<string>()
        };
        if (statuses.Length == 0) return;
        var selected = await DisplayActionSheetAsync(AppStrings.ChequeChangeStatus, AppStrings.Cancel, null, statuses);
        ChequeStatus? next = selected switch
        {
            var x when x == AppStrings.ChequeCleared => ChequeStatus.Cleared,
            var x when x == AppStrings.ChequeReturned => ChequeStatus.Returned,
            var x when x == AppStrings.ChequeSpent => ChequeStatus.Spent,
            var x when x == AppStrings.ChequeVoided => ChequeStatus.Voided,
            _ => null
        };
        if (next is null) return;
        Guid? accountId = null;
        if (next == ChequeStatus.Cleared)
        {
            var matchingAccounts = _viewModel.Accounts.Where(x => x.CurrencyCode == cheque.CurrencyCode).ToArray();
            if (matchingAccounts.Length == 0)
            { await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.ChequeAccountRequired, AppStrings.Ok); return; }
            var accountName = await DisplayActionSheetAsync(AppStrings.TransactionAccountPrompt, AppStrings.Cancel, null,
                matchingAccounts.Select(x => x.DisplayName).ToArray());
            accountId = matchingAccounts.FirstOrDefault(x => x.DisplayName == accountName)?.Id;
            if (accountId is null) return;
        }
        if (!await _viewModel.ChangeStatusAsync(cheque.Id, next.Value, accountId))
            await DisplayAlertAsync(AppStrings.ValidationError, _viewModel.StatusMessage, AppStrings.Ok);
    }

    private async void OnCalendarClicked(object? sender, EventArgs e) =>
        await DisplayAlertAsync(AppStrings.ChequeCalendar, _viewModel.GetCalendarText(), AppStrings.Ok);

    private async void OnDueSevenClicked(object? sender, EventArgs e) => await ShowDueAsync(7);
    private async void OnDueThirtyClicked(object? sender, EventArgs e) => await ShowDueAsync(30);

    private async Task ShowDueAsync(int days)
    {
        var due = _viewModel.Cheques.Where(x => x.Status is ChequeStatus.Pending or ChequeStatus.Returned &&
            x.DueDate >= DateOnly.FromDateTime(DateTime.Today) &&
            x.DueDate <= DateOnly.FromDateTime(DateTime.Today.AddDays(days))).ToArray();
        var message = due.Length == 0 ? AppStrings.ChequeEmpty :
            string.Join(Environment.NewLine, due.Select(x => $"{x.BankName} · {x.SerialNumber} · {x.DueDateText} · {x.AmountText}"));
        await DisplayAlertAsync(days == 7 ? AppStrings.ChequeDueSoon : AppStrings.ChequeDueMonth, message, AppStrings.Ok);
    }

    private bool TryParseAmount(string? value, out long amount)
    {
        amount = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var normalized = _numbers.ToLatinDigits(value).Replace("٬", string.Empty, StringComparison.Ordinal)
            .Replace(",", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal);
        return long.TryParse(normalized, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out amount) && amount > 0;
    }
}
