using Hesabyar.App.Services;
using Hesabyar.Application.Loans;
using Hesabyar.App.Resources.Strings;
using Hesabyar.App.ViewModels;
using Hesabyar.Localization;
using Hesabyar.Localization.Calendar;
using Hesabyar.Localization.Numbers;

namespace Hesabyar.App.Views;

public partial class LoansPage : ContentPage
{
    private readonly LoansViewModel _viewModel;
    private readonly IDateCalendarService _calendar;
    private readonly INumberNormalizer _numbers;

    public LoansPage(LoansViewModel viewModel, ILocalizationService localization,
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
        if (_viewModel.Accounts.Count == 0)
        { await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.LoanNoAccount, AppStrings.Ok); return; }
        var directionText = await DisplayActionSheetAsync(AppStrings.LoanDirectionPrompt, AppStrings.Cancel, null,
            AppStrings.LoanGiven, AppStrings.LoanReceived);
        if (directionText != AppStrings.LoanGiven && directionText != AppStrings.LoanReceived) return;
        var name = await DisplayPromptAsync(AppStrings.LoanName, AppStrings.LoanName, AppStrings.Save, AppStrings.Cancel, maxLength: 120);
        if (string.IsNullOrWhiteSpace(name)) return;
        var accountText = await DisplayActionSheetAsync(AppStrings.TransactionAccountPrompt, AppStrings.Cancel, null,
            _viewModel.Accounts.Select(x => x.DisplayName).ToArray());
        var account = _viewModel.Accounts.FirstOrDefault(x => x.DisplayName == accountText);
        if (account is null) return;
        var currencyCode = string.IsNullOrWhiteSpace(currencyCode) ? "IRR" : currencyCode;
        var amountText = await DisplayPromptAsync(AppStrings.TransactionAmountPrompt, $"{AppStrings.TransactionAmountPrompt} ({currencyCode})",
            AppStrings.Save, AppStrings.Cancel, keyboard: Keyboard.Numeric);
        if (!MoneyDisplayService.TryParse(amountText, currencyCode, out var principal))
        { await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.LoanInvalid, AppStrings.Ok); return; }
        var startText = await DisplayPromptAsync(AppStrings.LoanStartDate, AppStrings.LoanStartDate, AppStrings.Save, AppStrings.Cancel);
        if (!_calendar.TryParse(startText, out var startDate))
        { await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.LoanInvalid, AppStrings.Ok); return; }
        var scheduleText = await DisplayActionSheetAsync(AppStrings.LoanSchedulePrompt, AppStrings.Cancel, null,
            AppStrings.LoanAnnuity, AppStrings.LoanManual);
        if (scheduleText != AppStrings.LoanAnnuity && scheduleText != AppStrings.LoanManual) return;
        var countText = await DisplayPromptAsync(AppStrings.LoanInstallmentCount, AppStrings.LoanInstallmentCount,
            AppStrings.Save, AppStrings.Cancel, keyboard: Keyboard.Numeric);
        if (!int.TryParse(_numbers.ToLatinDigits(countText ?? string.Empty), out var count) || count is < 1 or > 600)
        { await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.LoanInvalid, AppStrings.Ok); return; }
        var scheduleType = scheduleText == AppStrings.LoanAnnuity ? LoanScheduleType.Annuity : LoanScheduleType.Manual;
        var rateBasisPoints = 0;
        IReadOnlyList<ManualInstallmentRequest>? manual = null;
        if (scheduleType == LoanScheduleType.Annuity)
        {
            var rateText = await DisplayPromptAsync(AppStrings.LoanAnnualRate, AppStrings.LoanAnnualRate,
                AppStrings.Save, AppStrings.Cancel, keyboard: Keyboard.Numeric, initialValue: "0");
            if (!TryParseRate(rateText, out rateBasisPoints))
            { await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.LoanInvalid, AppStrings.Ok); return; }
        }
        else
        {
            var rows = new List<ManualInstallmentRequest>();
            for (var i = 1; i <= count; i++)
            {
                var dueText = await DisplayPromptAsync($"{AppStrings.LoanInstallmentNumber} {i}", AppStrings.LoanDueDate,
                    AppStrings.Save, AppStrings.Cancel);
                if (!_calendar.TryParse(dueText, out var due) || due <= startDate) return;
                var principalText = await DisplayPromptAsync(AppStrings.LoanInstallmentPrincipal, $"{AppStrings.LoanInstallmentPrincipal} ({currencyCode})",
                    AppStrings.Save, AppStrings.Cancel, keyboard: Keyboard.Numeric);
                if (!MoneyDisplayService.TryParse(principalText, currencyCode, out var part)) return;
                var interestText = await DisplayPromptAsync(AppStrings.LoanInstallmentInterest, $"{AppStrings.LoanInstallmentInterest} ({currencyCode})",
                    AppStrings.Save, AppStrings.Skip, keyboard: Keyboard.Numeric, initialValue: "0");
                var interest = 0L;
                if (!string.IsNullOrWhiteSpace(interestText) &&
                    !MoneyDisplayService.TryParse(interestText, currencyCode, out interest, allowZero: true)) return;
                rows.Add(new ManualInstallmentRequest(due, part, interest));
            }
            manual = rows;
        }
        var personText = await DisplayActionSheetAsync(AppStrings.LoanPerson, AppStrings.Skip, null,
            _viewModel.People.Select(x => x.Name).ToArray());
        var person = _viewModel.People.FirstOrDefault(x => x.Name == personText);
        var note = await DisplayPromptAsync(AppStrings.LoanNote, AppStrings.LoanNote, AppStrings.Save, AppStrings.Skip, maxLength: 500);
        var request = new CreateLoanRequest(name, directionText == AppStrings.LoanGiven ? LoanDirection.Given : LoanDirection.Received,
            account.Id, person?.Id, principal, startDate, scheduleType, count, rateBasisPoints, manual, note);
        if (!await _viewModel.CreateAsync(request))
            await DisplayAlertAsync(AppStrings.ValidationError, _viewModel.StatusMessage, AppStrings.Ok);
    }

    private async void OnLoanSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not LoanListItem loan) return;
        if (sender is CollectionView list) list.SelectedItem = null;
        var actions = loan.IsClosed
            ? new[] { AppStrings.LoanSchedule }
            : new[] { AppStrings.LoanPay, AppStrings.LoanSchedule };
        var action = await DisplayActionSheetAsync(loan.Name, AppStrings.Cancel, null, actions);
        if (action == AppStrings.LoanSchedule)
        {
            await DisplayAlertAsync(AppStrings.LoanSchedule, _viewModel.GetScheduleText(loan), AppStrings.Ok);
            return;
        }
        if (action != AppStrings.LoanPay) return;
        var paymentAccounts = _viewModel.Accounts.Where(x => x.CurrencyCode == loan.Loan.CurrencyCode).ToArray();
        if (paymentAccounts.Length == 0)
        { await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.LoanNoAccount, AppStrings.Ok); return; }
        var accountText = await DisplayActionSheetAsync(AppStrings.TransactionAccountPrompt, AppStrings.Cancel, null,
            paymentAccounts.Select(x => x.DisplayName).ToArray());
        var account = paymentAccounts.FirstOrDefault(x => x.DisplayName == accountText);
        if (account is null) return;
        var amountText = await DisplayPromptAsync(AppStrings.LoanPaymentAmount, $"{AppStrings.LoanPaymentAmount} ({loan.Loan.CurrencyCode})",
            AppStrings.Save, AppStrings.Cancel, keyboard: Keyboard.Numeric);
        if (!MoneyDisplayService.TryParse(amountText, loan.Loan.CurrencyCode, out var amount) || amount > loan.RemainingMinor)
        { await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.LoanInvalid, AppStrings.Ok); return; }
        if (!await _viewModel.PayAsync(loan.Id, account.Id, amount, DateOnly.FromDateTime(DateTime.Today)))
            await DisplayAlertAsync(AppStrings.ValidationError, _viewModel.StatusMessage, AppStrings.Ok);
    }

    private bool TryParseAmount(string? value, out long amount) =>
        TryParseOptionalAmount(value, out amount) && amount > 0;

    private bool TryParseOptionalAmount(string? value, out long amount)
    {
        amount = 0;
        if (string.IsNullOrWhiteSpace(value)) return true;
        var normalized = _numbers.ToLatinDigits(value).Replace("٬", string.Empty, StringComparison.Ordinal)
            .Replace(",", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal);
        return long.TryParse(normalized, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out amount) && amount >= 0;
    }

    private bool TryParseRate(string? value, out int basisPoints)
    {
        basisPoints = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var normalized = _numbers.ToLatinDigits(value).Replace(",", ".", StringComparison.Ordinal);
        if (!decimal.TryParse(normalized, System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out var percent) || percent < 0 || percent > 1000) return false;
        basisPoints = decimal.ToInt32(decimal.Round(percent * 100, 0, MidpointRounding.AwayFromZero));
        return true;
    }
}
