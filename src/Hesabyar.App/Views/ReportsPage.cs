using Hesabyar.App.Resources.Strings;
using Hesabyar.App.Services;
using Hesabyar.Infrastructure.Persistence;
using Hesabyar.Localization;
using Hesabyar.Localization.Calendar;
using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace Hesabyar.App.Views;

public sealed class ReportsPage : ContentPage
{
    private readonly ReportsService _reports;
    private readonly ReportsExportService _exports;
    private readonly IDateCalendarService _calendar;
    private readonly VerticalStackLayout _body = new() { Padding = new Thickness(20, 12), Spacing = 14 };
    private readonly Label _rangeLabel = new() { FontSize = 12, TextColor = Colors.Gray };
    private readonly Label _status = new() { FontSize = 13 };
    private FinancialReportSnapshot? _snapshot;
    private DateOnly _fromDate;
    private DateOnly _toDate;

    public ReportsPage(
        ReportsService reports,
        ReportsExportService exports,
        ILocalizationService localization,
        IDateCalendarService calendar)
    {
        _reports = reports;
        _exports = exports;
        _calendar = calendar;
        Title = AppStrings.ReportsTab;
        FlowDirection = localization.FlowDirection == TextDirection.RightToLeft
            ? Microsoft.Maui.FlowDirection.RightToLeft
            : Microsoft.Maui.FlowDirection.LeftToRight;

        var today = DateOnly.FromDateTime(DateTime.Today);
        var persianToday = calendar.CurrentCalendar == CalendarKind.Persian
            ? today
            : today;
        var currentPersian = GetPersianDate(persianToday);
        _fromDate = calendar.CurrentCalendar == CalendarKind.Persian
            ? GetPersianYearStart(currentPersian)
            : new DateOnly(today.Year, 1, 1);
        _toDate = today;

        var rangeButton = new Button { Text = AppStrings.ReportsRange };
        rangeButton.Clicked += OnRangeClicked;
        var csvButton = new Button { Text = AppStrings.ReportsExportCsv };
        csvButton.Clicked += OnExportCsvClicked;
        var excelButton = new Button { Text = AppStrings.ReportsExportExcel };
        excelButton.Clicked += OnExportExcelClicked;

        var toolbar = new HorizontalStackLayout { Spacing = 8, Children = { rangeButton, csvButton, excelButton } };
        _body.Children.Add(new Label { Text = AppStrings.ReportsHeading, FontSize = 26, FontAttributes = FontAttributes.Bold });
        _body.Children.Add(toolbar);
        _body.Children.Add(_rangeLabel);
        _body.Children.Add(_status);

        Content = new ScrollView { Content = _body };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
    }

    private PersianDate GetPersianDate(DateOnly date)
    {
        var persianCalendar = new System.Globalization.PersianCalendar();
        var value = date.ToDateTime(TimeOnly.MinValue);
        return new PersianDate(persianCalendar.GetYear(value), persianCalendar.GetMonth(value), persianCalendar.GetDayOfMonth(value));
    }

    private DateOnly GetPersianYearStart(PersianDate date)
    {
        var persianCalendar = new System.Globalization.PersianCalendar();
        return DateOnly.FromDateTime(persianCalendar.ToDateTime(date.Year, 1, 1, 0, 0, 0, 0));
    }

    private async Task LoadAsync()
    {
        _status.Text = string.Empty;
        try
        {
            _snapshot = await _reports.GetAsync(_fromDate, _toDate);
            RenderReport(_snapshot);
        }
        catch (Exception)
        {
            _snapshot = null;
            _status.Text = AppStrings.ReportsLoadFailed;
        }
    }

    private async void OnRangeClicked(object? sender, EventArgs e)
    {
        var from = await DisplayPromptAsync(AppStrings.ReportsFromDate, AppStrings.ReminderDateFormat,
            AppStrings.Save, AppStrings.Cancel, initialValue: _calendar.Format(_fromDate));
        if (from is null) return;
        var to = await DisplayPromptAsync(AppStrings.ReportsToDate, AppStrings.ReminderDateFormat,
            AppStrings.Save, AppStrings.Cancel, initialValue: _calendar.Format(_toDate));
        if (to is null) return;
        if (!_calendar.TryParse(from, out var fromDate) || !_calendar.TryParse(to, out var toDate) || fromDate > toDate)
        {
            await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.ReportsDateInvalid, AppStrings.Ok);
            return;
        }
        _fromDate = fromDate;
        _toDate = toDate;
        await LoadAsync();
    }

    private async void OnExportCsvClicked(object? sender, EventArgs e) => await ShareExportAsync(excel: false);
    private async void OnExportExcelClicked(object? sender, EventArgs e) => await ShareExportAsync(excel: true);

    private async Task ShareExportAsync(bool excel)
    {
        if (_snapshot is null)
        {
            await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.ReportsLoadFailed, AppStrings.Ok);
            return;
        }

        try
        {
            var path = excel
                ? await _exports.CreateExcelAsync(_snapshot)
                : await _exports.CreateCsvAsync(_snapshot);
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = excel ? AppStrings.ReportsExportExcel : AppStrings.ReportsExportCsv,
                File = new ShareFile(path)
            });
        }
        catch (Exception)
        {
            await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.ReportsExportFailed, AppStrings.Ok);
        }
    }

    private void RenderReport(FinancialReportSnapshot report)
    {
        while (_body.Children.Count > 4)
            _body.Children.RemoveAt(4);

        _rangeLabel.Text = $"{_calendar.Format(report.FromDate)} — {_calendar.Format(report.ToDate)}";

        AddMetric(AppStrings.ReportsIncome, report.IncomeMinor);
        AddMetric(AppStrings.ReportsExpenses, report.ExpenseMinor);
        AddMetric(AppStrings.ReportsNetCashFlow, checked(report.IncomeMinor - report.ExpenseMinor));
        AddMetric(AppStrings.ReportsAssets, report.AssetsMinor);
        AddMetric(AppStrings.ReportsLiabilities, report.LiabilitiesMinor);
        AddMetric(AppStrings.ReportsNetWorth, report.NetWorthMinor);

        AddSection(AppStrings.ReportsMonthlyTrend);
        if (report.MonthlyTrend.Count == 0)
            _body.Children.Add(new Label { Text = AppStrings.ReportsEmpty });
        else
        {
            var max = Math.Max(1L, report.MonthlyTrend.SelectMany(x => new[] { x.IncomeMinor, x.ExpenseMinor }).Max());
            foreach (var month in report.MonthlyTrend)
            {
                var item = new VerticalStackLayout { Spacing = 4 };
                item.Children.Add(new Label { Text = month.Label, FontAttributes = FontAttributes.Bold });
                item.Children.Add(CreateBar($"{AppStrings.ReportsIncome}: {MoneyDisplayService.Format(month.IncomeMinor)}", month.IncomeMinor, max));
                item.Children.Add(CreateBar($"{AppStrings.ReportsExpenses}: {MoneyDisplayService.Format(month.ExpenseMinor)}", month.ExpenseMinor, max));
                _body.Children.Add(item);
            }
        }

        AddSection(AppStrings.ReportsByCategory);
        AddBars(report.ExpensesByCategory.Select(x => (x.Name, x.AmountMinor)).ToArray());

        AddSection(AppStrings.ReportsAccounts);
        foreach (var account in report.Accounts)
            _body.Children.Add(new Label
            {
                Text = $"{account.Name} · {account.Type} · {MoneyDisplayService.Format(account.BalanceMinor)}",
                FontSize = 13
            });

        AddSection(AppStrings.ReportsPeople);
        var people = report.People.Where(x => x.BalanceMinor != 0).ToArray();
        if (people.Length == 0)
            _body.Children.Add(new Label { Text = AppStrings.ReportsEmpty });
        foreach (var person in people)
            _body.Children.Add(new Label
            {
                Text = $"{person.Name} · {MoneyDisplayService.Format(person.BalanceMinor)}",
                FontSize = 13
            });
    }

    private void AddMetric(string title, long amount)
    {
        _body.Children.Add(new Border
        {
            Padding = 14,
            Stroke = new SolidColorBrush(Color.FromArgb("#E2E2E2")),
            StrokeThickness = 1,
            Content = new VerticalStackLayout
            {
                Spacing = 4,
                Children =
                {
                    new Label { Text = title, FontSize = 13, TextColor = Colors.Gray },
                    new Label { Text = MoneyDisplayService.Format(amount), FontSize = 20, FontAttributes = FontAttributes.Bold }
                }
            }
        });
    }

    private void AddSection(string title)
    {
        _body.Children.Add(new Label
        {
            Text = title,
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            Margin = new Thickness(0, 10, 0, 2)
        });
    }

    private void AddBars(IReadOnlyList<(string Name, long Amount)> items)
    {
        if (items.Count == 0)
        {
            _body.Children.Add(new Label { Text = AppStrings.ReportsEmpty });
            return;
        }
        var max = Math.Max(1L, items.Max(x => x.Amount));
        foreach (var item in items)
            _body.Children.Add(CreateBar($"{item.Name} · {MoneyDisplayService.Format(item.Amount)}", item.Amount, max));
    }

    private static View CreateBar(string title, long value, long maximum)
    {
        var ratio = Math.Clamp((double)value / maximum, 0d, 1d);
        return new VerticalStackLayout
        {
            Spacing = 3,
            Children =
            {
                new Label { Text = title, FontSize = 12 },
                new ProgressBar { Progress = ratio, ProgressColor = Color.FromArgb("#2D5B50"), HeightRequest = 8 }
            }
        };
    }
}
