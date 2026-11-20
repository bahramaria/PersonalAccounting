using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Media;
using Microsoft.Maui.Storage;
using Hesabyar.App.Services;
using Hesabyar.App.Resources.Strings;
using Hesabyar.Application.Transactions;
using Hesabyar.App.ViewModels;
using Hesabyar.Domain.Accounts;
using Hesabyar.Domain.Ledger;
using Hesabyar.Localization;
using Hesabyar.Localization.Calendar;
using Hesabyar.Infrastructure.Persistence;

namespace Hesabyar.App.Views;

public partial class TransactionsPage : ContentPage
{
    private readonly TransactionsViewModel _viewModel;
    private readonly DocumentAttachmentService _attachments;
    private readonly CategorySuggestionService _categorySuggestions;
    private readonly IDateCalendarService _calendar;
    private string? _searchText;
    private DateOnly? _fromDate;
    private DateOnly? _toDate;
    private Guid? _accountFilterId;
    private Guid? _categoryFilterId;
    private long? _minimumAmountMinor;
    private long? _maximumAmountMinor;
    private TransactionSortOrder _sortOrder = TransactionSortOrder.NewestFirst;

    public TransactionsPage(TransactionsViewModel viewModel, DocumentAttachmentService attachments, CategorySuggestionService categorySuggestions, ILocalizationService localization, IDateCalendarService calendar)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _attachments = attachments;
        _categorySuggestions = categorySuggestions;
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

    private async void OnRemainingItemsThresholdReached(object? sender, EventArgs e) =>
        await _viewModel.LoadMoreAsync();

    public Task OpenQuickAddAsync() => ShowEditorAsync(null);
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

    private async void OnSortClicked(object? sender, EventArgs e)
    {
        var selected = await DisplayActionSheetAsync(AppStrings.TransactionSort, AppStrings.Cancel, null,
            AppStrings.TransactionSortNewest, AppStrings.TransactionSortOldest);
        if (selected == AppStrings.TransactionSortNewest) _sortOrder = TransactionSortOrder.NewestFirst;
        else if (selected == AppStrings.TransactionSortOldest) _sortOrder = TransactionSortOrder.OldestFirst;
        else return;
        await ReloadAsync();
    }

    private async void OnClearFiltersClicked(object? sender, EventArgs e)
    {
        _fromDate = null;
        _toDate = null;
        _accountFilterId = null;
        _categoryFilterId = null;
        _minimumAmountMinor = null;
        _maximumAmountMinor = null;
        _searchText = null;
        TransactionSearchBar.Text = string.Empty;
        await ReloadAsync();
    }

    private Task ReloadAsync() => _viewModel.LoadAsync(
        _searchText, _fromDate, _toDate, _accountFilterId, _categoryFilterId,
        _minimumAmountMinor, _maximumAmountMinor, _sortOrder);

    private string? FormatPersianDate(DateOnly? date)
    {
        if (date is null) return null;
        return _calendar.Format(date.Value);
    }

    private bool TryParseOptionalDate(string value, out DateOnly? date)
    {
        date = null;
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (!_calendar.TryParse(value, out var parsed)) return false;
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
        var baseLabels = items.ToDictionary(x => x.Id, x => $"{x.Name} ({x.CurrencyCode})");
        var duplicateLabels = baseLabels.Values
            .GroupBy(x => x, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        var labelsById = items.ToDictionary(
            x => x.Id,
            x => duplicateLabels.Contains(baseLabels[x.Id])
                ? $"{baseLabels[x.Id]} · {x.Id.ToString("N")[..4]}"
                : baseLabels[x.Id]);
        var labels = new[] { allLabel }.Concat(items.Select(x => labelsById[x.Id])).ToArray();
        var selected = await DisplayActionSheetAsync(title, AppStrings.Cancel, null, labels);
        if (selected == AppStrings.Cancel) return selectedId;
        if (selected == allLabel) return null;
        return items.FirstOrDefault(x => labelsById[x.Id] == selected)?.Id ?? selectedId;
    }

    private async void OnTransactionSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not TransactionListItem item) return;
        if (sender is CollectionView list) list.SelectedItem = null;
        var supportsStandardActions = item.Kind is TransactionKind.Expense
            or TransactionKind.Income
            or TransactionKind.Transfer
            or TransactionKind.CurrencyExchange;
        var actions = supportsStandardActions
            ? new[] { AppStrings.TransactionEdit, AppStrings.TransactionDuplicate, AppStrings.TransactionDelete, AppStrings.TransactionManageTags, AppStrings.Attachments }
            : new[] { AppStrings.TransactionManageTags, AppStrings.Attachments };
        var action = await DisplayActionSheetAsync(item.KindName, AppStrings.Cancel, null, actions);
        if (action == AppStrings.Attachments)
        {
            await ManageTransactionAttachmentsAsync(item.Id);
        }
        else if (action == AppStrings.TransactionManageTags)
        {
            var tags = await _viewModel.GetTagsAsync();
            var currentIds = await _viewModel.GetTransactionTagIdsAsync(item.Id);
            var currentNames = tags.Where(x => currentIds.Contains(x.Id)).Select(x => x.Name);
            var text = await DisplayPromptAsync(AppStrings.TransactionManageTags, AppStrings.TransactionTagsPrompt,
                AppStrings.Save, AppStrings.Cancel, initialValue: string.Join(", ", currentNames), maxLength: 400);
            if (text is null) return;
            var selectedNames = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var selectedTags = tags.Where(x => selectedNames.Contains(x.Name, StringComparer.OrdinalIgnoreCase)).ToArray();
            if (selectedTags.Length != selectedNames.Length)
            {
                await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.TransactionTagsPrompt, AppStrings.Ok);
                return;
            }
            if (!await _viewModel.SetTransactionTagsAsync(item.Id, selectedTags.Select(x => x.Id).ToArray()))
                await DisplayAlertAsync(AppStrings.ValidationError, _viewModel.StatusMessage, AppStrings.Ok);
        }
        else if (action == AppStrings.TransactionEdit) await ShowEditorAsync(item);
        else if (action == AppStrings.TransactionDuplicate)
        {
            if (!await _viewModel.DuplicateAsync(item.Id))
                await DisplayAlertAsync(AppStrings.ValidationError, _viewModel.StatusMessage, AppStrings.Ok);
            else await ReloadAsync();
        }
        else if (action == AppStrings.TransactionDelete)
        {
            var confirmed = await DisplayAlertAsync(AppStrings.TransactionDelete, AppStrings.TransactionDeleteConfirmation,
                AppStrings.TransactionDelete, AppStrings.Cancel);
            if (confirmed)
            {
                if (!await _viewModel.DeleteAsync(item.Id))
                    await DisplayAlertAsync(AppStrings.ValidationError, _viewModel.StatusMessage, AppStrings.Ok);
                else await ReloadAsync();
            }
        }
    }


    private async Task ManageTransactionAttachmentsAsync(Guid transactionId)
    {
        while (true)
        {
            try
            {
                var items = await _attachments.GetAsync(transactionId: transactionId);
                var labelsById = items.ToDictionary(
                    x => x.Id,
                    x => $"{x.FileName} · {x.Id.ToString("N")[..4]}");
                var choices = new[] { AppStrings.AttachmentAdd }.Concat(labelsById.Values).ToArray();
                var selected = await DisplayActionSheetAsync(AppStrings.Attachments, AppStrings.Cancel, null, choices);
                if (selected == AppStrings.Cancel || selected is null) return;
                if (selected == AppStrings.AttachmentAdd)
                {
                    var addChoice = await DisplayActionSheetAsync(AppStrings.AttachmentAdd, AppStrings.Cancel, null,
                        AppStrings.AttachmentChooseFile, AppStrings.AttachmentCapturePhoto);
                    if (addChoice == AppStrings.AttachmentChooseFile)
                    {
                        var file = await FilePicker.Default.PickAsync();
                        if (file is null) continue;
                        await using var input = await file.OpenReadAsync();
                        await _attachments.AddAsync(input, file.FileName, transactionId: transactionId);
                    }
                    else if (addChoice == AppStrings.AttachmentCapturePhoto)
                    {
                        try
                        {
                            var photo = await MediaPicker.Default.CapturePhotoAsync();
                            if (photo is null) continue;
                            await using var input = await photo.OpenReadAsync();
                            await _attachments.AddAsync(input, photo.FileName, transactionId: transactionId);
                        }
                        catch (FeatureNotSupportedException)
                        {
                            await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.AttachmentCameraUnavailable, AppStrings.Ok);
                        }
                        catch (FeatureNotEnabledException)
                        {
                            await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.AttachmentCameraUnavailable, AppStrings.Ok);
                        }
                        catch (PermissionException)
                        {
                            await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.AttachmentCameraUnavailable, AppStrings.Ok);
                        }
                    }
                    continue;
                }

                var item = items.FirstOrDefault(x => labelsById[x.Id] == selected);
                if (item is null) continue;
                var action = await DisplayActionSheetAsync(item.FileName, AppStrings.Cancel, null,
                    AppStrings.AttachmentOpen, AppStrings.AttachmentDelete);
                if (action == AppStrings.AttachmentOpen)
                {
                    var bytes = await _attachments.ReadAsync(item.Id);
                    var extension = Path.GetExtension(item.FileName);
                    var path = Path.Combine(FileSystem.CacheDirectory, $"attachment-{item.Id:N}{extension}");
                    await File.WriteAllBytesAsync(path, bytes);
                    await Launcher.Default.OpenAsync(new OpenFileRequest(item.FileName, new ReadOnlyFile(path)));
                }
                else if (action == AppStrings.AttachmentDelete)
                {
                    var confirmed = await DisplayAlertAsync(AppStrings.AttachmentDelete, item.FileName,
                        AppStrings.AttachmentDelete, AppStrings.Cancel);
                    if (confirmed) await _attachments.DeleteAsync(item.Id);
                }
            }
            catch
            {
                await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.AttachmentFailed, AppStrings.Ok);
                return;
            }
        }
    }

    private async Task ShowEditorAsync(TransactionListItem? existing)
    {
        TransactionKind? kind = existing?.Kind;
        if (kind is null)
        {
            var selected = await DisplayActionSheetAsync(AppStrings.AddTransaction, AppStrings.Cancel, null,
                AppStrings.Expense, AppStrings.Income, AppStrings.Transfer, AppStrings.CurrencyExchange);
            kind = selected switch
            {
                var x when x == AppStrings.Expense => TransactionKind.Expense,
                var x when x == AppStrings.Income => TransactionKind.Income,
                var x when x == AppStrings.Transfer => TransactionKind.Transfer,
                var x when x == AppStrings.CurrencyExchange => TransactionKind.CurrencyExchange,
                _ => null
            };
        }
        if (kind is null) return;

        var accountChoices = _viewModel.Accounts.Where(x =>
            kind is TransactionKind.Transfer or TransactionKind.CurrencyExchange ? x.Type == AccountType.Asset :
            x.Type == AccountType.Asset || kind == TransactionKind.Expense && x.Type == AccountType.Liability).ToArray();
        var accountId = await SelectAsync(AppStrings.TransactionAccountPrompt, accountChoices, existing?.AccountId);
        if (accountId is null) return;

        var currencyCode = _viewModel.Accounts.First(x => x.Id == accountId).CurrencyCode;
        var amountText = await DisplayPromptAsync(AppStrings.TransactionAmountPrompt,
            $"{AppStrings.TransactionAmountPrompt} ({currencyCode})", AppStrings.Save, AppStrings.Cancel,
            initialValue: existing is null ? null : MoneyDisplayService.FormatInput(existing.AmountMinor, currencyCode),
            keyboard: Keyboard.Numeric);
        if (amountText is null) return;
        if (!MoneyDisplayService.TryParse(amountText, currencyCode, out var amount))
        {
            await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.TransactionAmountInvalid, AppStrings.Ok);
            return;
        }

        var secondChoices = kind switch
        {
            TransactionKind.Transfer => _viewModel.Accounts
                .Where(x => x.Type == AccountType.Asset && x.Id != accountId && x.CurrencyCode == currencyCode).ToArray(),
            TransactionKind.CurrencyExchange => _viewModel.Accounts
                .Where(x => x.Type == AccountType.Asset && x.Id != accountId && x.CurrencyCode != currencyCode).ToArray(),
            TransactionKind.Expense => _viewModel.ExpenseCategories.Where(x => x.CurrencyCode == currencyCode).ToArray(),
            _ => _viewModel.IncomeCategories.Where(x => x.CurrencyCode == currencyCode).ToArray()
        };
        var secondId = await SelectAsync(kind == TransactionKind.Transfer ? AppStrings.TransactionDestinationPrompt : AppStrings.TransactionCategoryPrompt,
            secondChoices, existing?.SecondaryId);
        if (secondId is null) return;

        long? counterAmountMinor = null;
        if (kind == TransactionKind.CurrencyExchange)
        {
            var destinationAmountText = await DisplayPromptAsync(
                AppStrings.TransactionExchangeDestinationAmountPrompt,
                $"{AppStrings.TransactionExchangeDestinationAmountPrompt} ({_viewModel.Accounts.First(x => x.Id == secondId).CurrencyCode})",
                AppStrings.Save, AppStrings.Cancel,
                initialValue: existing?.CounterAmountMinor is long counterMinor
                    ? MoneyDisplayService.FormatInput(counterMinor, _viewModel.Accounts.First(x => x.Id == secondId).CurrencyCode)
                    : null,
                keyboard: Keyboard.Numeric);
            if (destinationAmountText is null) return;
            if (!MoneyDisplayService.TryParse(destinationAmountText,
                    _viewModel.Accounts.First(x => x.Id == secondId).CurrencyCode, out var destinationAmount))
            {
                await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.TransactionAmountInvalid, AppStrings.Ok);
                return;
            }
            counterAmountMinor = destinationAmount;
        }

        var defaultGregorian = existing?.BusinessDate ?? DateOnly.FromDateTime(DateTime.Today);
        var defaultDate = _calendar.Format(defaultGregorian);
        var dateText = await DisplayPromptAsync(AppStrings.TransactionDatePrompt, "YYYY/MM/DD", AppStrings.Save, AppStrings.Cancel, initialValue: defaultDate);
        if (dateText is null || !_calendar.TryParse(dateText, out var date))
        {
            await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.TransactionDateInvalid, AppStrings.Ok);
            return;
        }

        var description = await DisplayPromptAsync(AppStrings.TransactionDescriptionPrompt, AppStrings.TransactionDescriptionPrompt,
            AppStrings.Save, AppStrings.Skip, initialValue: existing?.Description, maxLength: 500);
        CategorySuggestion? suggestion = null;
        var acceptedSuggestion = false;
        if (kind is TransactionKind.Expense or TransactionKind.Income && secondId is Guid selectedCategoryId)
        {
            suggestion = await _categorySuggestions.SuggestAsync(description, kind.Value, accountId.Value, currencyCode);
            if (suggestion is not null)
            {
                var confidence = Math.Round(suggestion.Confidence * 100).ToString(System.Globalization.CultureInfo.InvariantCulture);
                var message = string.Format(AppStrings.CategorySuggestionReason, suggestion.MatchText, confidence);
                var useSuggestion = await DisplayAlertAsync(
                    $"{AppStrings.CategorySuggestionTitle}: {suggestion.CategoryName}",
                    message,
                    AppStrings.CategorySuggestionUse,
                    AppStrings.CategorySuggestionKeep);
                if (useSuggestion)
                {
                    secondId = suggestion.CategoryId;
                    acceptedSuggestion = true;
                }
            }
        }

        if (!await _viewModel.SaveAsync(existing?.Id, kind.Value, date, amount, accountId.Value, secondId, description,
                counterAmountMinor: counterAmountMinor))
        {
            await DisplayAlertAsync(AppStrings.ValidationError, _viewModel.StatusMessage, AppStrings.Ok);
            return;
        }

        if (acceptedSuggestion && suggestion is not null)
            await _categorySuggestions.RecordAcceptedSuggestionAsync(suggestion.RuleId);

        if (kind is TransactionKind.Expense or TransactionKind.Income &&
            secondId is Guid finalCategoryId && !string.IsNullOrWhiteSpace(description))
        {
            var remember = await DisplayAlertAsync(AppStrings.CategoryRuleRememberTitle,
                AppStrings.CategoryRuleRememberMessage, AppStrings.CategoryRuleRemember, AppStrings.Cancel);
            if (remember)
            {
                var phrase = await DisplayPromptAsync(AppStrings.CategoryRuleRememberTitle,
                    AppStrings.CategoryRulePhrasePrompt, AppStrings.Save, AppStrings.Cancel,
                    initialValue: description, maxLength: 120);
                if (!string.IsNullOrWhiteSpace(phrase))
                {
                    try
                    {
                        var currentAccountName = _viewModel.Accounts.First(x => x.Id == accountId.Value).Name;
                        var scope = await DisplayActionSheetAsync(AppStrings.CategoryRuleRememberTitle, AppStrings.Cancel, null,
                            currentAccountName, AppStrings.CategoryRuleAllAccounts);
                        if (scope == AppStrings.Cancel || scope is null) return;
                        var ruleAccountId = scope == AppStrings.CategoryRuleAllAccounts ? null : accountId.Value;
                        await _categorySuggestions.LearnAsync(phrase, kind.Value, finalCategoryId, ruleAccountId);
                    }
                    catch
                    {
                        await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.CategoryRuleSaveFailed, AppStrings.Ok);
                    }
                }
            }
        }
        await ReloadAsync();
    }

    private async Task<Guid?> SelectAsync(string title, IEnumerable<TransactionChoice> choices, Guid? selectedId)
    {
        var items = choices.ToArray();
        if (items.Length == 0) return null;
        var labelsById = items.ToDictionary(x => x.Id, x => $"{x.Name} ({x.CurrencyCode})");
        var selectedLabel = selectedId is Guid id && labelsById.TryGetValue(id, out var label) ? label : null;
        var labels = selectedLabel is null ? items.Select(x => labelsById[x.Id]).ToArray()
            : new[] { selectedLabel }.Concat(items.Where(x => labelsById[x.Id] != selectedLabel).Select(x => labelsById[x.Id])).ToArray();
        var selected = await DisplayActionSheetAsync(title, AppStrings.Cancel, null, labels);
        return selected == AppStrings.Cancel ? null : items.FirstOrDefault(x => labelsById[x.Id] == selected)?.Id;
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
