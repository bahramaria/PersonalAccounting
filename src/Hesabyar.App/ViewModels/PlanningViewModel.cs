using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Hesabyar.Application.Accounts;
using Hesabyar.Application.Planning;
using Hesabyar.App.Resources.Strings;
using Hesabyar.Domain.Accounts;
using Hesabyar.Localization.Calendar;
using Microsoft.Extensions.DependencyInjection;

namespace Hesabyar.App.ViewModels;

public sealed class PlanningViewModel(IServiceScopeFactory scopeFactory, IDateCalendarService calendar) : ObservableObject
{
    private string _statusMessage = string.Empty;
    public ObservableCollection<BudgetListItem> Budgets { get; } = [];
    public ObservableCollection<TagSnapshot> Tags { get; } = [];
    public ObservableCollection<FiscalPeriodSnapshot> FiscalPeriods { get; } = [];
    public ObservableCollection<PlanningCategory> Categories { get; } = [];
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var provider = scope.ServiceProvider;
            var today = DateOnly.FromDateTime(DateTime.Today);
            var budgets = await provider.GetRequiredService<BudgetService>()
                .GetAsync(today.AddYears(-1), today.AddYears(2), cancellationToken);
            Budgets.Clear();
            foreach (var item in budgets)
                Budgets.Add(new BudgetListItem(item.CategoryName, item.LimitMinor.ToString("N0"),
                    item.SpentMinor.ToString("N0"), item.CurrencyCode, item.UsageRatio,
                    $"{calendar.Format(item.PeriodStart)} — {calendar.Format(item.PeriodEnd)}"));
            Tags.Clear();
            foreach (var tag in await provider.GetRequiredService<TagService>().GetAsync(cancellationToken))
                Tags.Add(tag);
            FiscalPeriods.Clear();
            foreach (var period in await provider.GetRequiredService<FiscalPeriodService>().GetAsync(cancellationToken))
                FiscalPeriods.Add(period);
            var accounts = await provider.GetRequiredService<AccountService>().GetAccountsAsync(cancellationToken: cancellationToken);
            Categories.Clear();
            if (accounts.IsSuccess && accounts.Value is not null)
                foreach (var item in accounts.Value.Where(x => !x.Account.IsArchived &&
                    x.Account.Kind == AccountKind.Category && x.Account.Type == AccountType.Expense))
                    Categories.Add(new PlanningCategory(item.Account.Id, item.Account.Name, item.Account.CurrencyCode));
            StatusMessage = string.Empty;
        }
        catch { StatusMessage = AppStrings.PlanningLoadFailed; }
    }

    public async Task<bool> CreateBudgetAsync(Guid categoryId, DateOnly start, DateOnly end, long limit, string currency)
    {
        using var scope = scopeFactory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<BudgetService>()
            .CreateAsync(new CreateBudgetRequest(categoryId, start, end, limit, currency));
        if (!result.IsSuccess) { StatusMessage = result.Error.Message ?? AppStrings.BudgetSaveFailed; return false; }
        await LoadAsync(); return true;
    }

    public async Task<bool> SaveTagAsync(string name)
    {
        using var scope = scopeFactory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<TagService>().SaveAsync(null, name, null);
        if (!result.IsSuccess) { StatusMessage = result.Error.Message ?? AppStrings.TagSaveFailed; return false; }
        await LoadAsync(); return true;
    }

    public async Task<bool> CreateFiscalPeriodAsync(string name, DateOnly start, DateOnly end)
    {
        using var scope = scopeFactory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<FiscalPeriodService>()
            .CreateAsync(new CreateFiscalPeriodRequest(name, start, end));
        if (!result.IsSuccess) { StatusMessage = result.Error.Message ?? AppStrings.FiscalPeriodSaveFailed; return false; }
        await LoadAsync(); return true;
    }

    public async Task<bool> CloseFiscalPeriodAsync(Guid id)
    {
        using var scope = scopeFactory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<FiscalPeriodService>().CloseAsync(id);
        if (!result.IsSuccess) { StatusMessage = result.Error.Message ?? AppStrings.FiscalPeriodSaveFailed; return false; }
        await LoadAsync(); return true;
    }
}

public sealed record PlanningCategory(Guid Id, string Name, string CurrencyCode);
public sealed record BudgetListItem(string CategoryName, string LimitText, string SpentText,
    string CurrencyCode, decimal UsageRatio, string PeriodText);
