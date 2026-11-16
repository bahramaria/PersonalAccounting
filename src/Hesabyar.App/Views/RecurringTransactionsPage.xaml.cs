using Hesabyar.App.Resources.Strings;
using Hesabyar.App.ViewModels;
using Hesabyar.Localization;
using Hesabyar.Localization.Calendar;
using Hesabyar.Localization.Numbers;

namespace Hesabyar.App.Views;

public partial class RecurringTransactionsPage : ContentPage
{
    private readonly RecurringTransactionsViewModel _viewModel;
    private readonly IDateCalendarService _calendar;
    private readonly INumberNormalizer _numbers;

    public RecurringTransactionsPage(
        RecurringTransactionsViewModel viewModel,
        ILocalizationService localization,
        IDateCalendarService calendar,
        INumberNormalizer numbers)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _calendar = calendar;
        _numbers = numbers;
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

    private async void OnAddClicked(object? sender, EventArgs e) => await PromptScheduleAsync();

    private async void OnPrepareDueClicked(object? sender, EventArgs e)
    {
        var count = await _viewModel.PrepareDueOccurrencesAsync(DateOnly.FromDateTime(DateTime.Today));
        await DisplayAlertAsync(AppStrings.RecurringPrepareDue, string.Format(AppStrings.RecurringPreparedCount, count), AppStrings.Ok);
    }

    private async void OnScheduleSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not RecurringScheduleItemViewModel schedule)
            return;
        if (sender is CollectionView list)
            list.SelectedItem = null;

        var pauseAction = schedule.IsPaused ? AppStrings.RecurringResume : AppStrings.RecurringPause;
        var action = await DisplayActionSheetAsync(
            schedule.Title, AppStrings.Cancel, null,
            AppStrings.RecurringOccurrences, pauseAction);

        if (action == pauseAction)
        {
            if (!await _viewModel.SetPausedAsync(schedule.Id, !schedule.IsPaused))
                await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.RecurringActionFailed, AppStrings.Ok);
            return;
        }

        if (action == AppStrings.RecurringOccurrences)
            await ShowOccurrencesAsync(schedule);
    }

    private async Task ShowOccurrencesAsync(RecurringScheduleItemViewModel schedule)
    {
        var occurrences = await _viewModel.GetOccurrencesAsync(schedule.Id);
        if (occurrences.Count == 0)
        {
            await DisplayAlertAsync(AppStrings.RecurringOccurrences, AppStrings.RecurringNoOccurrences, AppStrings.Ok);
            return;
        }

        var labels = occurrences.Select(x =>
            $"{x.DateText} — {(x.Status == "Pending" ? AppStrings.RecurringPending : AppStrings.RecurringGenerated)}").ToArray();
        var selected = await DisplayActionSheetAsync(
            AppStrings.RecurringOccurrences, AppStrings.Cancel, null, labels);
        var index = Array.IndexOf(labels, selected);
        if (index < 0)
            return;

        var occurrence = occurrences[index];
        if (occurrence.Status != "Pending")
        {
            await DisplayAlertAsync(AppStrings.RecurringOccurrences, AppStrings.RecurringAlreadyGenerated, AppStrings.Ok);
            return;
        }

        var confirmed = await DisplayAlertAsync(
            AppStrings.RecurringGenerate,
            string.Format(AppStrings.RecurringGenerateConfirm, occurrence.DateText),
            AppStrings.Continue,
            AppStrings.Cancel);
        if (confirmed && !await _viewModel.GenerateTransactionAsync(occurrence.Id))
            await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.RecurringActionFailed, AppStrings.Ok);
        else if (confirmed)
            await DisplayAlertAsync(AppStrings.RecurringGenerate, AppStrings.RecurringGenerated, AppStrings.Ok);
    }

    private async Task PromptScheduleAsync()
    {
        var title = await DisplayPromptAsync(
            AppStrings.RecurringAdd, AppStrings.RecurringTitle,
            AppStrings.Save, AppStrings.Cancel, maxLength: 160);
        if (string.IsNullOrWhiteSpace(title))
            return;

        var kindLabel = await DisplayActionSheetAsync(
            AppStrings.RecurringKind, AppStrings.Cancel, null,
            AppStrings.Expense, AppStrings.Income);
        var kind = kindLabel == AppStrings.Expense ? "Expense" :
            kindLabel == AppStrings.Income ? "Income" : null;
        if (kind is null)
            return;

        var accountChoices = _viewModel.Accounts
            .Where(x => kind == "Income" ? x.Type == "Asset" : x.Type is "Asset" or "Liability")
            .ToArray();
        if (accountChoices.Length == 0)
        {
            await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.RecurringNoAccount, AppStrings.Ok);
            return;
        }

        var accountLabels = accountChoices.Select(x => $"{x.Name} ({x.CurrencyCode})").ToArray();
        var accountLabel = await DisplayActionSheetAsync(
            AppStrings.TransactionAccountPrompt, AppStrings.Cancel, null, accountLabels);
        var accountIndex = Array.IndexOf(accountLabels, accountLabel);
        if (accountIndex < 0)
            return;
        var account = accountChoices[accountIndex];

        var categoryChoices = (await _viewModel.GetCategoriesAsync(kind))
            .Where(x => x.CurrencyCode == account.CurrencyCode).ToArray();
        if (categoryChoices.Length == 0)
        {
            await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.RecurringNoCategory, AppStrings.Ok);
            return;
        }

        var categoryLabels = categoryChoices.Select(x => x.Name).ToArray();
        var categoryLabel = await DisplayActionSheetAsync(
            AppStrings.TransactionCategoryPrompt, AppStrings.Cancel, null, categoryLabels);
        var categoryIndex = Array.IndexOf(categoryLabels, categoryLabel);
        if (categoryIndex < 0)
            return;
        var category = categoryChoices[categoryIndex];

        var amountText = await DisplayPromptAsync(
            AppStrings.TransactionAmountPrompt, AppStrings.TransactionAmountPrompt,
            AppStrings.Save, AppStrings.Cancel, keyboard: Keyboard.Numeric);
        if (!TryParseAmount(amountText, out var amount))
        {
            await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.TransactionAmountInvalid, AppStrings.Ok);
            return;
        }

        var startText = await DisplayPromptAsync(
            AppStrings.RecurringStartDate, AppStrings.ReminderDateFormat,
            AppStrings.Save, AppStrings.Cancel);
        if (!_calendar.TryParse(startText, out var startDate))
        {
            await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.ReminderDateInvalid, AppStrings.Ok);
            return;
        }

        var endText = await DisplayPromptAsync(
            AppStrings.RecurringEndDate, AppStrings.RecurringEndDateOptional,
            AppStrings.Save, AppStrings.Skip);
        DateOnly? endDate = null;
        if (!string.IsNullOrWhiteSpace(endText))
        {
            if (!_calendar.TryParse(endText, out var parsedEnd) || parsedEnd < startDate)
            {
                await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.RecurringEndDateInvalid, AppStrings.Ok);
                return;
            }
            endDate = parsedEnd;
        }

        var intervalText = await DisplayPromptAsync(
            AppStrings.RecurringInterval, AppStrings.RecurringIntervalFormat,
            AppStrings.Save, AppStrings.Cancel,
            initialValue: "1", keyboard: Keyboard.Numeric);
        if (!int.TryParse(_numbers.ToLatinDigits(intervalText ?? string.Empty), out var intervalCount) || intervalCount <= 0)
        {
            await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.RecurringIntervalInvalid, AppStrings.Ok);
            return;
        }

        var unitLabel = await DisplayActionSheetAsync(
            AppStrings.RecurringIntervalUnit, AppStrings.Cancel, null,
            AppStrings.RecurringDaily, AppStrings.RecurringWeekly,
            AppStrings.RecurringMonthly, AppStrings.RecurringYearly);
        var unit = unitLabel switch
        {
            var value when value == AppStrings.RecurringDaily => "Day",
            var value when value == AppStrings.RecurringWeekly => "Week",
            var value when value == AppStrings.RecurringMonthly => "Month",
            var value when value == AppStrings.RecurringYearly => "Year",
            _ => null
        };
        if (unit is null)
            return;

        if (!await _viewModel.CreateAsync(title, kind, account, category, amount, startDate, endDate, intervalCount, unit))
            await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.RecurringInvalid, AppStrings.Ok);
    }

    private bool TryParseAmount(string? value, out long amount)
    {
        amount = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var normalized = _numbers.ToLatinDigits(value)
            .Replace("٬", string.Empty, StringComparison.Ordinal)
            .Replace(",", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
        return long.TryParse(normalized, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out amount) && amount > 0;
    }
}
