using Hesabyar.App.Resources.Strings;
using Hesabyar.Infrastructure.Persistence;
using Hesabyar.Domain.Ledger;
using Hesabyar.Localization;

namespace Hesabyar.App.Views;

public sealed class CategorySuggestionRulesPage : ContentPage
{
    private readonly CategorySuggestionService _service;
    private readonly VerticalStackLayout _items = new() { Spacing = 10 };
    private readonly Label _status = new() { FontSize = 13 };

    public CategorySuggestionRulesPage(CategorySuggestionService service, ILocalizationService localization)
    {
        _service = service;
        Title = AppStrings.CategorySuggestionRules;
        FlowDirection = localization.FlowDirection == TextDirection.RightToLeft
            ? Microsoft.Maui.FlowDirection.RightToLeft
            : Microsoft.Maui.FlowDirection.LeftToRight;

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(20, 24),
                Spacing = 14,
                Children =
                {
                    new Label { Text = AppStrings.CategorySuggestionRules, FontSize = 24, FontAttributes = FontAttributes.Bold },
                    new Label { Text = AppStrings.CategorySuggestionRulesDescription, FontSize = 13 },
                    new Button { Text = AppStrings.CategoryRuleDeleteAll, Command = new Command(async () => await DeleteAllAsync()) },
                    _status,
                    _items
                }
            }
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
    }

    private async Task DeleteAllAsync()
    {
        var confirmed = await DisplayAlertAsync(AppStrings.CategoryRuleDeleteAll,
            AppStrings.CategoryRuleDeleteAllConfirm, AppStrings.CategoryRuleDeleteAll, AppStrings.Cancel);
        if (!confirmed) return;
        await _service.DeleteAllAsync();
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var rules = await _service.GetRulesAsync();
            _items.Clear();
            foreach (var rule in rules)
            {
                var kind = rule.TransactionKind == TransactionKind.Income ? AppStrings.Income : AppStrings.Expense;
                var scope = rule.AccountName ?? AppStrings.CategoryRuleAllAccounts;
                var status = rule.IsEnabled ? AppStrings.CategoryRuleEnabled : AppStrings.CategoryRuleDisabled;
                var card = new Border
                {
                    Padding = 14,
                    StrokeThickness = 1,
                    Stroke = new SolidColorBrush(Colors.Gray),
                    Content = new VerticalStackLayout
                    {
                        Spacing = 5,
                        Children =
                        {
                            new Label { Text = rule.MatchText, FontAttributes = FontAttributes.Bold, FontSize = 16 },
                            new Label { Text = $"{kind} · {rule.CategoryName} ({rule.CurrencyCode})" },
                            new Label { Text = $"{scope} · {status} · {AppStrings.CategoryRuleUsage}: {rule.UsageCount}", FontSize = 12 }
                        }
                    }
                };
                var tap = new TapGestureRecognizer();
                tap.Tapped += async (_, _) => await OnRuleTappedAsync(rule);
                card.GestureRecognizers.Add(tap);
                _items.Add(card);
            }
            _status.Text = rules.Count == 0 ? AppStrings.CategoryRuleEmpty : string.Empty;
        }
        catch
        {
            _status.Text = AppStrings.CategoryRuleLoadFailed;
        }
    }

    private async Task OnRuleTappedAsync(CategorySuggestionRuleSnapshot rule)
    {
        var toggleLabel = rule.IsEnabled ? AppStrings.CategoryRuleDisable : AppStrings.CategoryRuleEnable;
        var action = await DisplayActionSheetAsync(rule.MatchText, AppStrings.Cancel, null,
            AppStrings.CategoryRuleEdit, toggleLabel, AppStrings.CategoryRuleDelete);
        if (action == AppStrings.CategoryRuleEdit)
        {
            var phrase = await DisplayPromptAsync(AppStrings.CategoryRuleEdit,
                AppStrings.CategoryRulePhrasePrompt, AppStrings.Save, AppStrings.Cancel,
                initialValue: rule.MatchText, maxLength: 120);
            if (string.IsNullOrWhiteSpace(phrase)) return;

            var categories = (await _service.GetCategoriesAsync(rule.TransactionKind))
                .Where(x => x.CurrencyCode == rule.CurrencyCode).ToArray();
            var labelsById = categories.ToDictionary(x => x.Id, x => $"{x.Name} ({x.CurrencyCode}) · {x.Id.ToString("N")[..4]}");
            var labels = categories.Select(x => labelsById[x.Id]).ToArray();
            if (labels.Length == 0)
            {
                await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.CategoryRuleSaveFailed, AppStrings.Ok);
                return;
            }
            var selected = await DisplayActionSheetAsync(AppStrings.TransactionCategoryPrompt,
                AppStrings.Cancel, null, labels);
            if (selected == AppStrings.Cancel || selected is null) return;
            var category = categories.FirstOrDefault(x => labelsById[x.Id] == selected);
            if (category is null || !await _service.UpdateRuleAsync(rule.Id, phrase, category.Id))
            {
                await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.CategoryRuleSaveFailed, AppStrings.Ok);
                return;
            }
            await LoadAsync();
        }
        else if (action == toggleLabel)
        {
            await _service.SetEnabledAsync(rule.Id, !rule.IsEnabled);
            await LoadAsync();
        }
        else if (action == AppStrings.CategoryRuleDelete)
        {
            var confirmed = await DisplayAlertAsync(AppStrings.CategoryRuleDelete, rule.MatchText,
                AppStrings.CategoryRuleDelete, AppStrings.Cancel);
            if (confirmed)
            {
                await _service.DeleteAsync(rule.Id);
                await LoadAsync();
            }
        }
    }
}
