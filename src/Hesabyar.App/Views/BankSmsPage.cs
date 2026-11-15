using Hesabyar.Application.Accounts;
using Hesabyar.Application.Sms;
using Hesabyar.App.Resources.Strings;
using Hesabyar.Domain.Accounts;
using Hesabyar.Infrastructure.Persistence;
using Hesabyar.Localization;
using Hesabyar.Localization.Calendar;

namespace Hesabyar.App.Views;

public sealed class BankSmsPage : ContentPage
{
    private readonly BankSmsInboxService _inbox;
    private readonly AccountService _accounts;
    private readonly IDateCalendarService _calendar;
    private readonly VerticalStackLayout _items = new() { Spacing = 10 };
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
                    _items
                }
            }
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await ReloadAsync();
        var shared = SharedSmsContent.Take();
        if (!string.IsNullOrWhiteSpace(shared))
            await ImportTextAsync(shared);
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
                outcome == SmsImportOutcome.Added ? AppStrings.SmsAdded : AppStrings.SmsDuplicate,
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
                    Text = $"{item.TransactionType} · {item.AmountMinor?.ToString("N0") ?? AppStrings.SmsAmountUnknown} · {item.ParseStatus}",
                    FontSize = 12
                });
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
            initialValue: item.AmountMinor?.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var normalizedAmount = BankSmsParser.Normalize(amountText ?? string.Empty).Replace(",", string.Empty);
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

        var success = await _inbox.ConfirmAsync(item.Id, account.Value.Id, category.Value.Id, type, amount, date);
        if (!success)
        {
            await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.SmsConfirmFailed, AppStrings.Ok);
            return;
        }
        await DisplayAlertAsync(AppStrings.SmsHeading, AppStrings.SmsImported, AppStrings.Ok);
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
