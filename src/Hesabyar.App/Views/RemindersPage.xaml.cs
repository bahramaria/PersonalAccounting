using Hesabyar.App.Resources.Strings;
using Hesabyar.App.ViewModels;
using Hesabyar.Localization;
using Hesabyar.Localization.Calendar;
using Hesabyar.Localization.Numbers;

namespace Hesabyar.App.Views;

public partial class RemindersPage : ContentPage
{
    private readonly RemindersViewModel _viewModel;
    private readonly IDateCalendarService _calendar;
    private readonly INumberNormalizer _numbers;

    public RemindersPage(RemindersViewModel viewModel, ILocalizationService localization, IDateCalendarService calendar, INumberNormalizer numbers)
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

    private async void OnAddClicked(object? sender, EventArgs e) =>
        await PromptReminderAsync(null);

    private async void OnReminderSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not ReminderListItemViewModel reminder)
            return;
        if (sender is CollectionView list)
            list.SelectedItem = null;

        var actions = reminder.IsCompleted
            ? new[] { AppStrings.ReminderReopen, AppStrings.ReminderArchive }
            : new[] { AppStrings.ReminderComplete, AppStrings.ReminderEdit, AppStrings.ReminderArchive };
        var action = await DisplayActionSheetAsync(reminder.Title, AppStrings.Cancel, null, actions);

        if (action == AppStrings.ReminderComplete)
        {
            if (!await _viewModel.CompleteAsync(reminder.Id))
                await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.ReminderActionFailed, AppStrings.Ok);
        }
        else if (action == AppStrings.ReminderReopen)
        {
            if (!await _viewModel.ReopenAsync(reminder.Id))
                await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.ReminderActionFailed, AppStrings.Ok);
        }
        else if (action == AppStrings.ReminderEdit)
        {
            await PromptReminderAsync(reminder);
        }
        else if (action == AppStrings.ReminderArchive)
        {
            var confirmed = await DisplayAlertAsync(
                AppStrings.ReminderArchive,
                AppStrings.ReminderArchiveConfirm,
                AppStrings.Continue,
                AppStrings.Cancel);
            if (confirmed && !await _viewModel.ArchiveAsync(reminder.Id))
                await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.ReminderActionFailed, AppStrings.Ok);
        }
    }

    private async Task PromptReminderAsync(ReminderListItemViewModel? existing)
    {
        var title = await DisplayPromptAsync(
            existing is null ? AppStrings.ReminderAdd : AppStrings.ReminderEdit,
            AppStrings.ReminderTitle,
            AppStrings.Save,
            AppStrings.Cancel,
            initialValue: existing?.Title,
            maxLength: 160);
        if (string.IsNullOrWhiteSpace(title))
            return;

        var kinds = new[]
        {
            AppStrings.ReminderKindGeneral,
            AppStrings.ReminderKindBill,
            AppStrings.ReminderKindDebt,
            AppStrings.ReminderKindCheque,
            AppStrings.ReminderKindInstallment
        };
        var kind = await DisplayActionSheetAsync(
            AppStrings.ReminderKind,
            AppStrings.Cancel,
            null,
            kinds);
        if (string.IsNullOrWhiteSpace(kind) || kind == AppStrings.Cancel)
            return;

        var dueDateText = await DisplayPromptAsync(
            AppStrings.ReminderDueDate,
            AppStrings.ReminderDateFormat,
            AppStrings.Save,
            AppStrings.Cancel,
            initialValue: existing is null ? null : _calendar.Format(existing.DueDate));
        if (!_calendar.TryParse(dueDateText, out var dueDate))
        {
            await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.ReminderDateInvalid, AppStrings.Ok);
            return;
        }

        var notifyTime = await DisplayPromptAsync(
            AppStrings.ReminderNotifyTime,
            AppStrings.ReminderNotifyTimeFormat,
            AppStrings.Save,
            AppStrings.Skip,
            initialValue: existing?.NotifyTimeText,
            keyboard: Keyboard.Numeric);
        DateTimeOffset? notifyAtUtc = null;
        if (!string.IsNullOrWhiteSpace(notifyTime))
        {
            if (!TimeOnly.TryParseExact(_numbers.ToLatinDigits(notifyTime), "HH:mm", out var time))
            {
                await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.ReminderTimeInvalid, AppStrings.Ok);
                return;
            }

            var localDateTime = dueDate.ToDateTime(time);
            notifyAtUtc = new DateTimeOffset(localDateTime, TimeZoneInfo.Local.GetUtcOffset(localDateTime)).ToUniversalTime();
        }

        var note = await DisplayPromptAsync(
            AppStrings.ReminderNote,
            AppStrings.ReminderNote,
            AppStrings.Save,
            AppStrings.Skip,
            initialValue: existing?.Note,
            maxLength: 500);

        var saved = existing is null
            ? await _viewModel.CreateAsync(title, kind, dueDate, notifyAtUtc, note)
            : await _viewModel.UpdateAsync(existing.Id, title, kind, dueDate, notifyAtUtc, note);

        if (!saved)
            await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.ReminderInvalid, AppStrings.Ok);
    }
}
