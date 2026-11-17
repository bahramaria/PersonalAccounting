using Hesabyar.App.Resources.Strings;
using Hesabyar.App.ViewModels;
using Hesabyar.Localization;
using Hesabyar.Localization.Calendar;

namespace Hesabyar.App.Views;

public sealed class PlanningPage : ContentPage
{
    private readonly PlanningViewModel _viewModel;
    private readonly IDateCalendarService _calendar;
    private readonly VerticalStackLayout _content = new() { Spacing = 10, Padding = 18 };

    public PlanningPage(PlanningViewModel viewModel, ILocalizationService localization, IDateCalendarService calendar)
    {
        _viewModel = viewModel;
        _calendar = calendar;
        Title = AppStrings.PlanningTab;
        FlowDirection = localization.FlowDirection == TextDirection.RightToLeft
            ? Microsoft.Maui.FlowDirection.RightToLeft : Microsoft.Maui.FlowDirection.LeftToRight;
        Content = new ScrollView { Content = _content };
        Render();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
        Render();
    }

    private void Render()
    {
        _content.Children.Clear();
        _content.Children.Add(new Label { Text = AppStrings.PlanningHeading, FontSize = 25, FontAttributes = FontAttributes.Bold });
        AddSection(AppStrings.BudgetsHeading, AppStrings.AddBudget, OnAddBudgetClicked);
        if (_viewModel.Budgets.Count == 0) _content.Children.Add(new Label { Text = AppStrings.BudgetEmpty });
        foreach (var item in _viewModel.Budgets)
        {
            var cardContent = new VerticalStackLayout
            {
                Spacing = 7,
                Children =
                {
                    new Label { Text = item.CategoryName, FontAttributes = FontAttributes.Bold },
                    new Label { Text = $"{item.SpentText} / {item.LimitText} {item.CurrencyCode}" },
                    new ProgressBar { Progress = (double)Math.Clamp(item.UsageRatio, 0m, 1m) },
                    new Label { Text = item.PeriodText + (item.IsActive ? string.Empty : $" · {AppStrings.BudgetInactive}"), FontSize = 12, Opacity = 0.7 }
                }
            };
            var actions = new HorizontalStackLayout { Spacing = 6 };
            var edit = new Button { Text = AppStrings.BudgetEdit, CommandParameter = item.Id, FontSize = 12 };
            edit.Clicked += OnEditBudgetClicked;
            var toggle = new Button
            {
                Text = item.IsActive ? AppStrings.BudgetDeactivate : AppStrings.BudgetActivate,
                CommandParameter = item.Id,
                FontSize = 12
            };
            toggle.Clicked += OnToggleBudgetClicked;
            actions.Children.Add(edit);
            actions.Children.Add(toggle);
            cardContent.Children.Add(actions);
            _content.Children.Add(new Border { Padding = 12, Stroke = Colors.LightGray, Content = cardContent });
        }
        AddSection(AppStrings.TagsHeading, AppStrings.AddTag, OnAddTagClicked);
        foreach (var tag in _viewModel.Tags)
        {
            var row = new HorizontalStackLayout { Spacing = 8 };
            row.Children.Add(new Label
            {
                Text = tag.Color is null ? tag.Name : $"{tag.Name} · {tag.Color}",
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Fill
            });
            var edit = new Button { Text = AppStrings.EditTag, CommandParameter = tag.Id, FontSize = 12 };
            edit.Clicked += OnEditTagClicked;
            var archive = new Button { Text = AppStrings.ArchiveTag, CommandParameter = tag.Id, FontSize = 12 };
            archive.Clicked += OnArchiveTagClicked;
            row.Children.Add(edit);
            row.Children.Add(archive);
            _content.Children.Add(row);
        }
        AddSection(AppStrings.FiscalPeriodsHeading, AppStrings.AddFiscalPeriod, OnAddFiscalPeriodClicked);
        foreach (var period in _viewModel.FiscalPeriods)
        {
            var row = new HorizontalStackLayout { Spacing = 8 };
            row.Children.Add(new Label
            {
                Text = $"{period.Name} · {_calendar.Format(period.StartDate)} — {_calendar.Format(period.EndDate)}" +
                    (period.IsClosed ? $" · {AppStrings.FiscalPeriodClosed}" : string.Empty),
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Fill
            });
            if (!period.IsClosed)
            {
                var close = new Button { Text = AppStrings.FiscalPeriodClose, CommandParameter = period.Id, FontSize = 12 };
                close.Clicked += OnCloseFiscalPeriodClicked;
                row.Children.Add(close);
            }
            _content.Children.Add(row);
        }
        if (!string.IsNullOrWhiteSpace(_viewModel.StatusMessage))
            _content.Children.Add(new Label { Text = _viewModel.StatusMessage, TextColor = Colors.IndianRed });
    }

    private void AddSection(string title, string action, EventHandler handler)
    {
        _content.Children.Add(new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
            Children =
            {
                new Label { Text = title, FontSize = 19, FontAttributes = FontAttributes.Bold, VerticalOptions = LayoutOptions.Center },
                CreateActionButton(action, handler)
            }
        });
    }

    private static Button CreateActionButton(string text, EventHandler handler)
    {
        var button = new Button { Text = text, FontSize = 12, Padding = new Thickness(10, 5) };
        button.Clicked += handler;
        Grid.SetColumn(button, 1);
        return button;
    }

    private async void OnAddBudgetClicked(object? sender, EventArgs e)
    {
        if (_viewModel.Categories.Count == 0)
        {
            await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.BudgetCategoryPrompt, AppStrings.Ok);
            return;
        }
        var category = await SelectCategoryAsync();
        if (category is null) return;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var startText = await DisplayPromptAsync(AppStrings.BudgetStartPrompt, AppStrings.BudgetStartPrompt,
            AppStrings.Save, AppStrings.Cancel, initialValue: _calendar.Format(new DateOnly(today.Year, today.Month, 1)));
        if (startText is null || !_calendar.TryParse(startText, out var start)) return;
        var endText = await DisplayPromptAsync(AppStrings.BudgetEndPrompt, AppStrings.BudgetEndPrompt,
            AppStrings.Save, AppStrings.Cancel, initialValue: _calendar.Format(start.AddMonths(1).AddDays(-1)));
        if (endText is null || !_calendar.TryParse(endText, out var end)) return;
        var limitText = await DisplayPromptAsync(AppStrings.BudgetLimitPrompt, $"{AppStrings.BudgetLimitPrompt} ({category.CurrencyCode})",
            AppStrings.Save, AppStrings.Cancel, keyboard: Keyboard.Numeric);
        if (limitText is null || !TryParseAmount(limitText, out var limit) || limit <= 0)
        {
            await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.BudgetSaveFailed, AppStrings.Ok);
            return;
        }
        if (!await _viewModel.CreateBudgetAsync(category.Id, start, end, limit, category.CurrencyCode))
            await DisplayAlertAsync(AppStrings.ValidationError, _viewModel.StatusMessage, AppStrings.Ok);
        Render();
    }

    private async void OnEditBudgetClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: Guid id }) return;
        var item = _viewModel.Budgets.FirstOrDefault(x => x.Id == id);
        if (item is null) return;
        var text = await DisplayPromptAsync(AppStrings.BudgetEdit, $"{AppStrings.BudgetLimitPrompt} ({item.CurrencyCode})",
            AppStrings.Save, AppStrings.Cancel, initialValue: item.LimitText, keyboard: Keyboard.Numeric);
        if (text is null) return;
        if (!TryParseAmount(text, out var limit) || limit <= 0 ||
            !await _viewModel.UpdateBudgetLimitAsync(id, limit))
            await DisplayAlertAsync(AppStrings.ValidationError,
                string.IsNullOrWhiteSpace(_viewModel.StatusMessage) ? AppStrings.BudgetSaveFailed : _viewModel.StatusMessage,
                AppStrings.Ok);
        Render();
    }

    private async void OnToggleBudgetClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: Guid id }) return;
        var item = _viewModel.Budgets.FirstOrDefault(x => x.Id == id);
        if (item is null) return;
        if (item.IsActive)
        {
            var confirmed = await DisplayAlertAsync(AppStrings.BudgetDeactivate,
                AppStrings.BudgetDeactivateConfirm, AppStrings.BudgetDeactivate, AppStrings.Cancel);
            if (!confirmed) return;
        }
        if (!await _viewModel.SetBudgetActiveAsync(id, !item.IsActive))
            await DisplayAlertAsync(AppStrings.ValidationError, _viewModel.StatusMessage, AppStrings.Ok);
        Render();
    }

    private async Task<PlanningCategory?> SelectCategoryAsync()
    {
        var choices = _viewModel.Categories.ToArray();
        var selected = await DisplayActionSheetAsync(AppStrings.BudgetCategoryPrompt, AppStrings.Cancel, null,
            choices.Select(x => x.Name).ToArray());
        return choices.FirstOrDefault(x => x.Name == selected);
    }

    private async void OnAddTagClicked(object? sender, EventArgs e)
    {
        var name = await DisplayPromptAsync(AppStrings.AddTag, AppStrings.TagNamePrompt,
            AppStrings.Save, AppStrings.Cancel, maxLength: 40);
        if (string.IsNullOrWhiteSpace(name)) return;
        var color = await DisplayPromptAsync(AppStrings.TagColorPrompt, AppStrings.TagColorPrompt,
            AppStrings.Save, AppStrings.Cancel, maxLength: 16);
        if (color is null) return;
        if (!await _viewModel.SaveTagAsync(name, color: color))
            await DisplayAlertAsync(AppStrings.ValidationError, _viewModel.StatusMessage, AppStrings.Ok);
        Render();
    }

    private async void OnEditTagClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: Guid id }) return;
        var tag = _viewModel.Tags.FirstOrDefault(x => x.Id == id);
        if (tag is null) return;
        var name = await DisplayPromptAsync(AppStrings.EditTag, AppStrings.TagNamePrompt,
            AppStrings.Save, AppStrings.Cancel, initialValue: tag.Name, maxLength: 40);
        if (string.IsNullOrWhiteSpace(name)) return;
        var color = await DisplayPromptAsync(AppStrings.TagColorPrompt, AppStrings.TagColorPrompt,
            AppStrings.Save, AppStrings.Cancel, initialValue: tag.Color, maxLength: 16);
        if (color is null) return;
        if (!await _viewModel.SaveTagAsync(name, id, color))
            await DisplayAlertAsync(AppStrings.ValidationError, _viewModel.StatusMessage, AppStrings.Ok);
        Render();
    }

    private async void OnArchiveTagClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: Guid id }) return;
        var confirmed = await DisplayAlertAsync(AppStrings.ArchiveTag, AppStrings.ArchiveTagConfirm,
            AppStrings.ArchiveTag, AppStrings.Cancel);
        if (!confirmed) return;
        if (!await _viewModel.ArchiveTagAsync(id))
            await DisplayAlertAsync(AppStrings.ValidationError, _viewModel.StatusMessage, AppStrings.Ok);
        Render();
    }

    private async void OnAddFiscalPeriodClicked(object? sender, EventArgs e)
    {
        var name = await DisplayPromptAsync(AppStrings.AddFiscalPeriod, AppStrings.FiscalPeriodNamePrompt,
            AppStrings.Save, AppStrings.Cancel, maxLength: 80);
        if (string.IsNullOrWhiteSpace(name)) return;
        var startText = await DisplayPromptAsync(AppStrings.FiscalPeriodStartPrompt, AppStrings.FiscalPeriodStartPrompt,
            AppStrings.Save, AppStrings.Cancel);
        if (startText is null || !_calendar.TryParse(startText, out var start)) return;
        var endText = await DisplayPromptAsync(AppStrings.FiscalPeriodEndPrompt, AppStrings.FiscalPeriodEndPrompt,
            AppStrings.Save, AppStrings.Cancel);
        if (endText is null || !_calendar.TryParse(endText, out var end)) return;
        if (!await _viewModel.CreateFiscalPeriodAsync(name, start, end))
            await DisplayAlertAsync(AppStrings.ValidationError, _viewModel.StatusMessage, AppStrings.Ok);
        Render();
    }

    private async void OnCloseFiscalPeriodClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: Guid id }) return;
        var confirmed = await DisplayAlertAsync(AppStrings.FiscalPeriodClose,
            AppStrings.FiscalPeriodClose + "?", AppStrings.Save, AppStrings.Cancel);
        if (confirmed && !await _viewModel.CloseFiscalPeriodAsync(id))
            await DisplayAlertAsync(AppStrings.ValidationError, _viewModel.StatusMessage, AppStrings.Ok);
        Render();
    }

    private static bool TryParseAmount(string value, out long amount)
    {
        var normalized = new System.Text.StringBuilder(value.Length);
        foreach (var ch in value)
            normalized.Append(ch switch { >= '۰' and <= '۹' => (char)('0' + ch - '۰'), >= '٠' and <= '٩' => (char)('0' + ch - '٠'), _ => ch });
        return long.TryParse(normalized.ToString().Replace(",", "").Replace("٬", "").Replace(" ", ""),
            System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out amount);
    }
}
