using Hesabyar.Application.Accounts;
using Hesabyar.Application.Sms;
using Hesabyar.App.Resources.Strings;
using Hesabyar.Domain.Accounts;
using Hesabyar.Infrastructure.Persistence;
using Hesabyar.Localization;
using Hesabyar.Localization.Calendar;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;

namespace Hesabyar.App.Views;

public sealed class BankSmsPage : ContentPage
{
    private readonly BankSmsInboxService _inbox;
    private readonly AccountService _accounts;
    private readonly IDateCalendarService _calendar;
    private readonly VerticalStackLayout _items = new() { Spacing = 10 };
    private readonly Button _autoImportButton;
    private bool _loading;

    public BankSmsPage(
        BankSmsInboxService inbox,
        AccountService accounts,
        IDateCalendarService calendar,
        ILocalizationService localization)
    {
        _inbox = inbox;
        _accounts = accounts;
        _calendar = calendar;
        Title = AppStrings.SmsTab;
        FlowDirection = localization.FlowDirection == TextDirection.RightToLeft
            ? Microsoft.Maui.FlowDirection.RightToLeft
            : Microsoft.Maui.FlowDirection.LeftToRight;

        var pasteButton = new Button { Text = AppStrings.SmsPaste };
        pasteButton.Clicked += OnPasteClicked;
        _autoImportButton = new Button();
        _autoImportButton.Clicked += OnAutoImportClicked;
        UpdateAutoImportButton();
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 18,
                Spacing = 12,
                Children =
                {
                    new Label { Text = AppStrings.SmsHeading, FontSize = 24, FontAttributes = FontAttributes.Bold },
                    new Label { Text = AppStrings.SmsPrivacyNote, FontSize = 13 },
                    pasteButton,
                    _autoImportButton,
                    _items
                }
            }
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        UpdateAutoImportButton();
        await ReloadAsync();
        var shared = SharedSmsContent.Take();
        if (!string.IsNullOrWhiteSpace(shared))
            await ImportTextAsync(shared);
    }


    private void UpdateAutoImportButton() =>
        _autoImportButton.Text = Preferences.Default.Get("sms-auto-import-enabled", false)
            ? AppStrings.SmsDisableAutoImport
            : AppStrings.SmsAutoImport;

    private async void OnAutoImportClicked(object? sender, EventArgs e)
    {
        if (Preferences.Default.Get("sms-auto-import-enabled", false))
        {
            Preferences.Default.Set("sms-auto-import-enabled", false);
            UpdateAutoImportButton();
            await DisplayAlertAsync(AppStrings.SmsPermissionTitle, AppStrings.SmsAutoImportDisabled, AppStrings.Ok);
            return;
        }

#if ANDROID
        var accepted = await DisplayAlertAsync(
            AppStrings.SmsPermissionTitle, AppStrings.SmsPermissionExplain,
            AppStrings.SmsEnable, AppStrings.Cancel);
        if (!accepted) return;

        var permission = await Permissions.RequestAsync<ReceiveSmsPermission>();
        if (permission != PermissionStatus.Granted)
        {
            await DisplayAlertAsync(AppStrings.SmsPermissionTitle, AppStrings.SmsPermissionDenied, AppStrings.Ok);
            return;
        }

        Preferences.Default.Set("sms-auto-import-enabled", true);
        UpdateAutoImportButton();
        await DisplayAlertAsync(AppStrings.SmsPermissionTitle, AppStrings.SmsAutoImportEnabled, AppStrings.Ok);
#else
        await DisplayAlertAsync(AppStrings.SmsPermissionTitle, AppStrings.SmsAndroidOnly, AppStrings.Ok);
#endif
    }

    private async void OnPasteClicked(object? sender, EventArgs e)
    {
        var text = await Clipboard.Default.GetTextAsync();
        if (string.IsNullOrWhiteSpace(text))
            text = await DisplayPromptAsync(AppStrings.SmsBodyPrompt, AppStrings.SmsBodyPrompt,
                AppStrings.Save, AppStrings.Cancel, maxLength: 4000);
        if (!string.IsNullOrWhiteSpace(text))
            await ImportTextAsync(text);
    }

    private async Task ImportTextAsync(string body)
    {
        var sender = await DisplayPromptAsync(AppStrings.SmsSenderPrompt, AppStrings.SmsSenderPrompt,
            AppStrings.Save, AppStrings.Cancel, maxLength: 120);
        if (sender is null)
            return;

        try
        {
            var outcome = await _inbox.ImportAsync(sender, body);
            await DisplayAlertAsync(AppStrings.SmsHeading,
                outcome == SmsImportOutcome.Added ? AppStrings.SmsAdded : outcome == SmsImportOutcome.Duplicate ? AppStrings.SmsDuplicate : AppStrings.SmsIgnored,
                AppStrings.Ok);
            await ReloadAsync();
        }
        catch (ArgumentException)
        {
            await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.SmsBodyInvalid, AppStrings.Ok);
        }
        catch
        {
            await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.SmsLoadFailed, AppStrings.Ok);
        }
    }

    private async Task ReloadAsync()
    {
        if (_loading) return;
        _loading = true;
        try
        {
            _items.Children.Clear();
            var pending = await _inbox.GetPendingAsync();
            if (pending.Count == 0)
            {
                _items.Children.Add(new Label { Text = AppStrings.SmsEmpty });
                return;
            }

            foreach (var item in pending)
            {
                var card = new VerticalStackLayout { Spacing = 6 };
                card.Children.Add(new Label
                {
                    Text = string.IsNullOrWhiteSpace(item.Sender) ? AppStrings.SmsUnknownSender : item.Sender,
                    FontAttributes = FontAttributes.Bold
                });
                card.Children.Add(new Label { Text = item.Body, FontSize = 13, LineBreakMode = LineBreakMode.WordWrap });
                card.Children.Add(new Label
                {
                    Text = $"{(item.TransactionType == BankSmsTransactionType.Income ? AppStrings.Income : item.TransactionType == BankSmsTransactionType.Expense ? AppStrings.Expense : AppStrings.SmsTypeUnknown)} · {item.AmountMinor?.ToString("N0") ?? AppStrings.SmsAmountUnknown} · {(item.AmountUnitKnown ? string.Empty : AppStrings.SmsUnitUnknown)} · {(item.ParseStatus == BankSmsParseStatus.Parsed ? AppStrings.SmsParsed : item.ParseStatus == BankSmsParseStatus.Unsupported ? AppStrings.SmsUnsupported : AppStrings.SmsNeedsReview)}",
                    FontSize = 12
                });
                if (item.BalanceMinor.HasValue || !string.IsNullOrWhiteSpace(item.CardLastFour) ||
                    !string.IsNullOrWhiteSpace(item.DateText) || !string.IsNullOrWhiteSpace(item.TransactionTime))
                {
                    var details = new List<string>();
                    if (item.BalanceMinor is long balanceValue)
                        details.Add($"{AppStrings.SmsBalance}: {balanceValue:N0}");
                    if (!item.AmountUnitKnown)
                        details.Add(AppStrings.SmsUnitUnknown);
                    if (!string.IsNullOrWhiteSpace(item.CardLastFour))
                        details.Add($"{AppStrings.SmsCardLastFour}: {item.CardLastFour}");
                    if (!string.IsNullOrWhiteSpace(item.DateText))
                        details.Add($"{AppStrings.SmsTransactionDate}: {item.DateText}");
                    if (!string.IsNullOrWhiteSpace(item.TransactionTime))
                        details.Add($"{AppStrings.SmsTransactionTime}: {item.TransactionTime}");
                    card.Children.Add(new Label { Text = string.Join(" · ", details), FontSize = 12 });
                }
                var actions = new HorizontalStackLayout { Spacing = 8 };
                var confirm = new Button { Text = AppStrings.SmsReviewAndConfirm };
                confirm.Clicked += async (_, _) => await ConfirmAsync(item);
                var ignore = new Button { Text = AppStrings.SmsIgnore };
                ignore.Clicked += async (_, _) =>
                {
                    await _inbox.IgnoreAsync(item.Id);
                    await ReloadAsync();
                };
                actions.Children.Add(confirm);
                actions.Children.Add(ignore);
                card.Children.Add(actions);
                _items.Children.Add(new Border { Padding = 12, Stroke = Colors.Gray, Content = card });
            }
        }
        catch
        {
            _items.Children.Clear();
            _items.Children.Add(new Label { Text = AppStrings.SmsLoadFailed });
        }
        finally { _loading = false; }
    }

    private async Task ConfirmAsync(BankSmsInboxRecord item)
    {
        var typeOptions = new[] { AppStrings.Expense, AppStrings.Income };
        var selectedType = item.TransactionType switch
        {
            BankSmsTransactionType.Expense => AppStrings.Expense,
            BankSmsTransactionType.Income => AppStrings.Income,
            _ => await DisplayActionSheetAsync(AppStrings.SmsTransactionTypePrompt, AppStrings.Cancel, null, typeOptions)
        };
        if (selectedType == AppStrings.Cancel || !typeOptions.Contains(selectedType))
            return;
        var type = selectedType == AppStrings.Income
            ? BankSmsTransactionType.Income : BankSmsTransactionType.Expense;

        var amountText = await DisplayPromptAsync(AppStrings.TransactionAmountPrompt, AppStrings.TransactionAmountPrompt,
            AppStrings.Save, AppStrings.Cancel,
            initialValue: item.AmountUnitKnown ? item.AmountMinor?.ToString(System.Globalization.CultureInfo.InvariantCulture) : null);
        var normalizedAmount = BankSmsParser.Normalize(amountText ?? string.Empty).Replace(",", string.Empty).Replace("٬", string.Empty).Replace("،", string.Empty).Replace(" ", string.Empty);
        if (!long.TryParse(normalizedAmount, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var amount) || amount <= 0)
            return;

        var accountResult = await _accounts.GetAccountsAsync();
        if (!accountResult.IsSuccess || accountResult.Value is null)
            return;
        var availableAccounts = accountResult.Value.Where(x => !x.Account.IsArchived &&
            (type == BankSmsTransactionType.Income
                ? x.Account.Type == AccountType.Asset
                : x.Account.Type is AccountType.Asset or AccountType.Liability)).ToArray();
        var account = await ChooseAsync(AppStrings.TransactionAccountPrompt,
            availableAccounts.Select(x => (x.Account.Id, x.Account.Name)).ToArray());
        if (account is null) return;

        var expectedCategoryType = type == BankSmsTransactionType.Income ? AccountType.Income : AccountType.Expense;
        var categories = accountResult.Value.Where(x => !x.Account.IsArchived &&
            x.Account.Kind == AccountKind.Category && x.Account.Type == expectedCategoryType).ToArray();
        var category = await ChooseAsync(AppStrings.TransactionCategoryPrompt,
            categories.Select(x => (x.Account.Id, x.Account.Name)).ToArray());
        if (category is null) return;

        var today = DateOnly.FromDateTime(DateTime.Now);
        var dateText = await DisplayPromptAsync(AppStrings.TransactionDatePrompt, "YYYY/MM/DD",
            AppStrings.Save, AppStrings.Cancel, initialValue: _calendar.Format(today));
        if (dateText is null || !_calendar.TryParse(dateText, out var date))
        {
            await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.TransactionDateInvalid, AppStrings.Ok);
            return;
        }

        if (await _inbox.HasPossibleManualDuplicateAsync(account.Value.Id, category.Value.Id, type, amount, date))
        {
            var proceed = await DisplayAlertAsync(AppStrings.SmsPossibleDuplicateTitle,
                AppStrings.SmsPossibleDuplicateMessage, AppStrings.SmsConfirmAnyway, AppStrings.Cancel);
            if (!proceed) return;
        }

        var success = await _inbox.ConfirmAsync(item.Id, account.Value.Id, category.Value.Id, type, amount, date);
        if (!success)
        {
            await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.SmsConfirmFailed, AppStrings.Ok);
            return;
        }
        await DisplayAlertAsync(AppStrings.SmsHeading, AppStrings.SmsImported, AppStrings.Ok);

        var balanceCheck = await _inbox.GetBalanceReconciliationAsync(item.Id);
        if (balanceCheck is { DifferenceMinor: not 0 })
        {
            var message = string.Format(
                AppStrings.SmsBalanceMismatchMessage,
                balanceCheck.SmsBalanceMinor.ToString("N0"),
                balanceCheck.LedgerBalanceMinor.ToString("N0"),
                balanceCheck.DifferenceMinor.ToString("N0"));
            var createAdjustment = await DisplayAlertAsync(
                AppStrings.SmsBalanceMismatchTitle, message,
                AppStrings.SmsCreateAdjustment, AppStrings.Cancel);
            if (createAdjustment)
            {
                var adjusted = await _inbox.CreateBalanceAdjustmentAsync(item.Id);
                await DisplayAlertAsync(
                    AppStrings.SmsBalanceMismatchTitle,
                    adjusted ? AppStrings.SmsAdjustmentCreated : AppStrings.SmsAdjustmentFailed,
                    AppStrings.Ok);
            }
        }
        await ReloadAsync();
    }

    private async Task<(Guid Id, string Name)?> ChooseAsync(string title, (Guid Id, string Name)[] choices)
    {
        if (choices.Length == 0)
            return null;
        var selected = await DisplayActionSheetAsync(title, AppStrings.Cancel, null,
            choices.Select(x => x.Name).ToArray());
        if (selected == AppStrings.Cancel)
            return null;
        var match = choices.FirstOrDefault(x => x.Name == selected);
        return match.Id == Guid.Empty ? null : match;
    }
}

public static class SharedSmsContent
{
    private static string? _pending;
    public static void Set(string? value) => Interlocked.Exchange(ref _pending, value);
    public static string? Take() => Interlocked.Exchange(ref _pending, null);
}
