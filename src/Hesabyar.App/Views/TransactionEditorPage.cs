using Hesabyar.App.Resources.Strings;
using Hesabyar.App.ViewModels;
using Hesabyar.Domain.Accounts;
using Hesabyar.Domain.Ledger;
using Hesabyar.Localization.Calendar;
using Hesabyar.Localization.Numbers;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;

namespace Hesabyar.App.Views;

public sealed class TransactionEditorPage : ContentPage
{
    private readonly TransactionsViewModel _viewModel;
    private readonly IDateCalendarService _calendar;
    private readonly INumberNormalizer _numberNormalizer;
    private readonly TransactionListItem? _existing;
    private readonly Picker _kindPicker = new() { Title = AppStrings.AddTransaction };
    private readonly Entry _amountEntry = new() { Keyboard = Keyboard.Numeric, Placeholder = "0" };
    private readonly Picker _accountPicker = new() { Title = AppStrings.TransactionAccountPrompt };
    private readonly Picker _secondaryPicker = new() { Title = AppStrings.TransactionCategoryPrompt };
    private readonly Entry _dateEntry = new() { Keyboard = Keyboard.Text };
    private readonly Entry _descriptionEntry = new() { MaxLength = 500 };
    private readonly Label _status = new() { TextColor = Colors.Red, FontSize = 13 };
    private readonly Button _saveButton = new() { Text = AppStrings.Save };
    private readonly TaskCompletionSource<bool> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _isFormattingAmount;
    private bool _isSaving;

    public Task<bool> Result => _completion.Task;

    public TransactionEditorPage(
        TransactionsViewModel viewModel,
        IDateCalendarService calendar,
        INumberNormalizer numberNormalizer,
        Microsoft.Maui.FlowDirection flowDirection,
        TransactionListItem? existing)
    {
        _viewModel = viewModel;
        _calendar = calendar;
        _numberNormalizer = numberNormalizer;
        _existing = existing;
        Title = existing is null ? AppStrings.AddTransaction : AppStrings.TransactionEdit;
        FlowDirection = flowDirection;

        _kindPicker.ItemsSource = new[] { AppStrings.Expense, AppStrings.Income, AppStrings.Transfer };
        _kindPicker.SelectedIndex = existing?.Kind switch
        {
            TransactionKind.Expense => 0,
            TransactionKind.Income => 1,
            TransactionKind.Transfer => 2,
            _ => 0
        };
        _kindPicker.IsEnabled = existing is null;
        _kindPicker.SelectedIndexChanged += OnKindChanged;
        _accountPicker.ItemDisplayBinding = new Binding(nameof(TransactionChoice.Name));
        _secondaryPicker.ItemDisplayBinding = new Binding(nameof(TransactionChoice.Name));
        _accountPicker.SelectedIndexChanged += OnAccountChanged;
        _amountEntry.TextChanged += OnAmountTextChanged;
        _amountEntry.Text = existing?.AmountMinor.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);
        _dateEntry.Text = _calendar.Format(existing?.BusinessDate ?? DateOnly.FromDateTime(DateTime.Today));
        _descriptionEntry.Text = existing?.Description;
        _descriptionEntry.Placeholder = AppStrings.TransactionDescriptionPrompt;
        _dateEntry.Placeholder = AppStrings.TransactionDatePrompt;
        _saveButton.Clicked += OnSaveClicked;

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 20,
                Spacing = 12,
                Children =
                {
                    new Label { Text = Title, FontSize = 24, FontAttributes = FontAttributes.Bold },
                    new Label { Text = AppStrings.AddTransaction, FontAttributes = FontAttributes.Bold },
                    _kindPicker,
                    new Label { Text = AppStrings.TransactionAmountPrompt, FontAttributes = FontAttributes.Bold },
                    _amountEntry,
                    new Label { Text = AppStrings.TransactionAccountPrompt, FontAttributes = FontAttributes.Bold },
                    _accountPicker,
                    new Label { Text = AppStrings.TransactionCategoryPrompt, FontAttributes = FontAttributes.Bold },
                    _secondaryPicker,
                    new Label { Text = AppStrings.TransactionDatePrompt, FontAttributes = FontAttributes.Bold },
                    _dateEntry,
                    new Label { Text = AppStrings.TransactionDescriptionPrompt, FontAttributes = FontAttributes.Bold },
                    _descriptionEntry,
                    _status,
                    new HorizontalStackLayout
                    {
                        Spacing = 12,
                        Children = { _saveButton, new Button { Text = AppStrings.Cancel, Command = new Command(OnCancel) } }
                    }
                }
            }
        };

        RefreshChoices();
    }

    private TransactionKind SelectedKind => _kindPicker.SelectedIndex switch
    {
        1 => TransactionKind.Income,
        2 => TransactionKind.Transfer,
        _ => TransactionKind.Expense
    };

    private void OnKindChanged(object? sender, EventArgs e) => RefreshChoices();

    private void OnAccountChanged(object? sender, EventArgs e)
    {
        if (SelectedKind == TransactionKind.Transfer)
            RefreshSecondaryChoices(_existing?.SecondaryId);
    }

    private void RefreshChoices()
    {
        var kind = SelectedKind;
        var accounts = _viewModel.Accounts.Where(x =>
            kind == TransactionKind.Transfer ? x.Type == AccountType.Asset :
            x.Type == AccountType.Asset || kind == TransactionKind.Expense && x.Type == AccountType.Liability).ToArray();

        _accountPicker.ItemsSource = accounts;
        _accountPicker.SelectedItem = accounts.FirstOrDefault(x => x.Id == _existing?.AccountId)
            ?? accounts.FirstOrDefault();

        RefreshSecondaryChoices(_existing?.SecondaryId);
    }

    private void RefreshSecondaryChoices(Guid? preferredId)
    {
        var kind = SelectedKind;
        var accountId = (_accountPicker.SelectedItem as TransactionChoice)?.Id;
        if (preferredId is null)
        {
            var key = kind switch
            {
                TransactionKind.Expense => "transactions.last-expense-category",
                TransactionKind.Income => "transactions.last-income-category",
                _ => string.Empty
            };
            if (key.Length > 0 && Guid.TryParse(Preferences.Get(key, string.Empty), out var rememberedId))
                preferredId = rememberedId;
        }
        var choices = kind switch
        {
            TransactionKind.Transfer => _viewModel.Accounts
                .Where(x => x.Type == AccountType.Asset && x.Id != accountId).ToArray(),
            TransactionKind.Expense => _viewModel.ExpenseCategories.ToArray(),
            _ => _viewModel.IncomeCategories.ToArray()
        };

        _secondaryPicker.ItemsSource = choices;
        _secondaryPicker.SelectedItem = choices.FirstOrDefault(x => x.Id == preferredId)
            ?? choices.FirstOrDefault();
    }

    private void OnAmountTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isFormattingAmount || string.IsNullOrEmpty(e.NewTextValue))
            return;

        var cursor = Math.Clamp(_amountEntry.CursorPosition, 0, e.NewTextValue.Length);
        var digitsBeforeCursor = _numberNormalizer.ToLatinDigits(e.NewTextValue[..cursor]).Count(char.IsAsciiDigit);
        var normalized = _numberNormalizer.ToLatinDigits(e.NewTextValue)
            .Replace(",", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
        if (!long.TryParse(normalized, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var amount))
            return;

        var formatted = amount.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);
        _isFormattingAmount = true;
        _amountEntry.Text = formatted;
        var digitCount = 0;
        var newCursor = formatted.Length;
        for (var index = 0; index < formatted.Length; index++)
        {
            if (!char.IsAsciiDigit(_numberNormalizer.ToLatinDigits(formatted[index].ToString())[0]))
                continue;
            digitCount++;
            if (digitCount == digitsBeforeCursor)
            {
                newCursor = index + 1;
                break;
            }
        }
        _amountEntry.CursorPosition = newCursor;
        _isFormattingAmount = false;
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        if (_isSaving)
            return;

        var normalized = _numberNormalizer.ToLatinDigits(_amountEntry.Text ?? string.Empty)
            .Replace(",", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
        if (!long.TryParse(normalized, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var amount) || amount <= 0)
        {
            await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.TransactionAmountInvalid, AppStrings.Ok);
            return;
        }

        if (_accountPicker.SelectedItem is not TransactionChoice account ||
            _secondaryPicker.SelectedItem is not TransactionChoice secondary)
        {
            await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.TransactionAccountPrompt, AppStrings.Ok);
            return;
        }

        if (!_calendar.TryParse(_dateEntry.Text ?? string.Empty, out var date))
        {
            await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.TransactionDateInvalid, AppStrings.Ok);
            return;
        }

        _isSaving = true;
        _saveButton.IsEnabled = false;
        try
        {
            var saved = await _viewModel.SaveAsync(
                _existing?.Id,
                SelectedKind,
                date,
                amount,
                account.Id,
                secondary.Id,
                _descriptionEntry.Text);
            if (!saved)
            {
                _status.Text = _viewModel.StatusMessage;
                return;
            }

            _completion.TrySetResult(true);
            await Navigation.PopModalAsync();
        }
        finally
        {
            _isSaving = false;
            _saveButton.IsEnabled = true;
        }
    }

    private async void OnCancel()
    {
        _completion.TrySetResult(false);
        await Navigation.PopModalAsync();
    }

    protected override void OnDisappearing()
    {
        _amountEntry.Text = string.Empty;
        _completion.TrySetResult(false);
        base.OnDisappearing();
    }
}
