using Hesabyar.App.Resources.Strings;
using Hesabyar.App.ViewModels;
using Hesabyar.Domain.Accounts;
using Hesabyar.Domain.Ledger;
using Hesabyar.Localization;
using Hesabyar.Localization.Calendar;

namespace Hesabyar.App.Views;

public partial class TransactionsPage : ContentPage
{
    private readonly TransactionsViewModel _viewModel;
    private readonly ICalendarService _calendar;
    private string? _searchText;
    private DateOnly? _fromDate;
    private DateOnly? _toDate;
    private Guid? _accountFilterId;
    private Guid? _categoryFilterId;
    private long? _minimumAmountMinor;
    private long? _maximumAmountMinor;

    public TransactionsPage(TransactionsViewModel viewModel, ILocalizationService localization, ICalendarService calendar)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _calendar = calendar;
        BindingContext = viewModel;
        FlowDirection = localization.FlowDirection == TextDirection.RightToLeft
            ? Microsoft.Maui.FlowDirection.RightToLeft : Microsoft.Maui.FlowDirection.LeftToRight;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await ReloadAsync();
    }
    private async void OnAddTransactionClicked(object? sender, EventArgs e) => await ShowEditorAsync(null);
    private async void OnSearchButtonPressed(object? sender, EventArgs e)
    {
        if (sender is SearchBar searchBar)
        {
            _searchText = searchBar.Text;
            await ReloadAsync();
        }
    }

    private async void OnFiltersClicked(object? sender, EventArgs e)
    {
        var fromText = await DisplayPromptAsync(AppStrings.TransactionFromDate, "YYYY/MM/DD", AppStrings.Save,
            AppStrings.Cancel, initialValue: FormatPersianDate(_fromDate));
        if (fromText is null) return;
        var toText = await DisplayPromptAsync(AppStrings.TransactionToDate, "YYYY/MM/DD", AppStrings.Save,
            AppStrings.Cancel, initialValue: FormatPersianDate(_toDate));
        if (toText is null) return;
        if (!TryParseOptionalDate(fromText, out var fromDate) || !TryParseOptionalDate(toText, out var toDate))
        {
            await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.TransactionDateInvalid, AppStrings.Ok);
            return;
        }
        if (fromDate is not null && toDate is not null && fromDate > toDate)
        {
            await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.TransactionDateRangeInvalid, AppStrings.Ok);
            return;
        }

        var accountId = await SelectFilterAsync(AppStrings.TransactionAccountFilter, _viewModel.Accounts, _accountFilterId);
        var categoryChoices = _viewModel.ExpenseCategories.Concat(_viewModel.IncomeCategories).ToArray();
        var categoryId = await SelectFilterAsync(AppStrings.TransactionCategoryFilter, categoryChoices, _categoryFilterId);
        var minimumText = await DisplayPromptAsync(AppStrings.TransactionMinimumAmount, AppStrings.TransactionMinimumAmount,
            AppStrings.Save, AppStrings.Cancel, initialValue: _minimumAmountMinor?.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (minimumText is null) return;
        var maximumText = await DisplayPromptAsync(AppStrings.TransactionMaximumAmount, AppStrings.TransactionMaximumAmount,
            AppStrings.Save, AppStrings.Cancel, initialValue: _maximumAmountMinor?.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (maximumText is null) return;
        if (!TryParseOptionalAmount(minimumText, out var minimum) || !TryParseOptionalAmount(maximumText, out var maximum))
        {
            await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.TransactionAmountInvalid, AppStrings.Ok);
            return;
        }
        if (minimum is not null && maximum is not null && minimum > maximum)
        {
            await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.TransactionAmountRangeInvalid, AppStrings.Ok);
            return;
        }

        _fromDate = fromDate;
        _toDate = toDate;
        _accountFilterId = accountId;
        _categoryFilterId = categoryId;
        _minimumAmountMinor = minimum;
        _maximumAmountMinor = maximum;
        await ReloadAsync();
    }

    private Task ReloadAsync() => _viewModel.LoadAsync(
        _searchText, _fromDate, _toDate, _accountFilterId, _categoryFilterId,
        _minimumAmountMinor, _maximumAmountMinor);

    private string? FormatPersianDate(DateOnly? date)
    {
        if (date is null) return null;
        var persian = _calendar.ToPersian(date.Value);
        return $"{persian.Year:0000}/{persian.Month:00}/{persian.Day:00}";
    }

    private bool TryParseOptionalDate(string value, out DateOnly? date)
    {
        date = null;
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (!TryParsePersianDate(value, out var parsed)) return false;
        date = parsed;
        return true;
    }

    private static bool TryParseOptionalAmount(string value, out long? amount)
    {
        amount = null;
        if (string.IsNullOrWhiteSpace(value)) return true;
        var normalized = NormalizeDigits(value).Replace(",", "").Replace("٬", "").Replace(" ", "");
        if (!long.TryParse(normalized, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
            return false;
        amount = parsed;
        return true;
    }

    private async Task<Guid?> SelectFilterAsync(string title, IEnumerable<TransactionChoice> choices, Guid? selectedId)
    {
        var items = choices.ToArray();
        var allLabel = AppStrings.TransactionFilterAll;
        var labels = new[] { allLabel }.Concat(items.Select(x => x.Name)).ToArray();
        var selected = await DisplayActionSheetAsync(title, AppStrings.Cancel, null, labels);
        if (selected == AppStrings.Cancel) return selectedId;
        if (selected == allLabel) return null;
        return items.FirstOrDefault(x => x.Name == selected)?.Id ?? selectedId;
    }

    private async void OnTransactionSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not TransactionListItem item) return;
        if (sender is CollectionView list) list.SelectedItem = null;
        var action = await DisplayActionSheetAsync(item.KindName, AppStrings.Cancel, null,
            AppStrings.TransactionEdit, AppStrings.TransactionDuplicate, AppStrings.TransactionDelete);
        if (action == AppStrings.TransactionEdit) await ShowEditorAsync(item);
        else if (action == AppStrings.TransactionDuplicate)
        {
            if (!await _viewModel.DuplicateAsync(item.Id))
                await DisplayAlertAsync(AppStrings.ValidationError, _viewModel.StatusMessage, AppStrings.Ok);
        }
        else if (action == AppStrings.TransactionDelete)
        {
            var confirmed = await DisplayAlertAsync(AppStrings.TransactionDelete, AppStrings.TransactionDeleteConfirmation,
                AppStrings.TransactionDelete, AppStrings.Cancel);
            if (confirmed && !await _viewModel.DeleteAsync(item.Id))
                await DisplayAlertAsync(AppStrings.ValidationError, _viewModel.StatusMessage, AppStrings.Ok);
        }
    }

    private async Task ShowEditorAsync(TransactionListItem? existing)
    {
        TransactionKind? kind = existing?.Kind;
        if (kind is null)
        {
            var selected = await DisplayActionSheetAsync(AppStrings.AddTransaction, AppStrings.Cancel, null,
                AppStrings.Expense, AppStrings.Income, AppStrings.Transfer);
            kind = selected switch
            {
                var x when x == AppStrings.Expense => TransactionKind.Expense,
                var x when x == AppStrings.Income => TransactionKind.Income,
                var x when x == AppStrings.Transfer => TransactionKind.Transfer,
                _ => null
            };
        }
        if (kind is null) return;

        var amountText = await DisplayPromptAsync(AppStrings.TransactionAmountPrompt, AppStrings.TransactionAmountPrompt,
            AppStrings.Save, AppStrings.Cancel,
            initialValue: existing?.AmountMinor.ToString(System.Globalization.CultureInfo.InvariantCulture),
            keyboard: Keyboard.Numeric);
        if (amountText is null) return;
        var normalized = NormalizeDigits(amountText).Replace(",", "").Replace("٬", "").Replace(" ", "");
        if (!long.TryParse(normalized, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var amount) || amount <= 0)
        {
            await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.TransactionAmountInvalid, AppStrings.Ok);
            return;
        }

        var accountChoices = _viewModel.Accounts.Where(x =>
            kind == TransactionKind.Transfer ? x.Type == AccountType.Asset :
            x.Type == AccountType.Asset || kind == TransactionKind.Expense && x.Type == AccountType.Liability).ToArray();
        var accountId = await SelectAsync(AppStrings.TransactionAccountPrompt, accountChoices, existing?.AccountId);
        if (accountId is null) return;

        var secondChoices = kind == TransactionKind.Transfer
            ? _viewModel.Accounts.Where(x => x.Type == AccountType.Asset && x.Id != accountId).ToArray()
            : kind == TransactionKind.Expense ? _viewModel.ExpenseCategories : _viewModel.IncomeCategories;
        var secondId = await SelectAsync(kind == TransactionKind.Transfer ? AppStrings.TransactionDestinationPrompt : AppStrings.TransactionCategoryPrompt,
            secondChoices, existing?.SecondaryId);
        if (secondId is null) return;

        var defaultGregorian = existing?.BusinessDate ?? DateOnly.FromDateTime(DateTime.Today);
        var defaultPersian = _calendar.ToPersian(defaultGregorian);
        var defaultDate = $"{defaultPersian.Year:0000}/{defaultPersian.Month:00}/{defaultPersian.Day:00}";
        var dateText = await DisplayPromptAsync(AppStrings.TransactionDatePrompt, "YYYY/MM/DD", AppStrings.Save, AppStrings.Cancel, initialValue: defaultDate);
        if (dateText is null || !TryParsePersianDate(dateText, out var date))
        {
            await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.TransactionDateInvalid, AppStrings.Ok);
            return;
        }

        var description = await DisplayPromptAsync(AppStrings.TransactionDescriptionPrompt, AppStrings.TransactionDescriptionPrompt,
            AppStrings.Save, AppStrings.Skip, initialValue: existing?.Description, maxLength: 500);
        if (!await _viewModel.SaveAsync(existing?.Id, kind.Value, date, amount, accountId.Value, secondId, description))
            await DisplayAlertAsync(AppStrings.ValidationError, _viewModel.StatusMessage, AppStrings.Ok);
    }

    private async Task<Guid?> SelectAsync(string title, IEnumerable<TransactionChoice> choices, Guid? selectedId)
    {
        var items = choices.ToArray();
        if (items.Length == 0) return null;
        var selectedName = items.FirstOrDefault(x => x.Id == selectedId)?.Name;
        var labels = selectedName is null ? items.Select(x => x.Name).ToArray()
            : new[] { selectedName }.Concat(items.Where(x => x.Name != selectedName).Select(x => x.Name)).ToArray();
        var selected = await DisplayActionSheetAsync(title, AppStrings.Cancel, null, labels);
        return selected == AppStrings.Cancel ? null : items.FirstOrDefault(x => x.Name == selected)?.Id;
    }

    private bool TryParsePersianDate(string value, out DateOnly gregorian)
    {
        gregorian = default;
        var parts = value.Trim().Replace('-', '/').Split('/');
        if (parts.Length != 3 ||
            !int.TryParse(NormalizeDigits(parts[0]), out var year) ||
            !int.TryParse(NormalizeDigits(parts[1]), out var month) ||
            !int.TryParse(NormalizeDigits(parts[2]), out var day))
            return false;
        try
        {
            gregorian = _calendar.ToGregorian(new PersianDate(year, month, day));
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
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
