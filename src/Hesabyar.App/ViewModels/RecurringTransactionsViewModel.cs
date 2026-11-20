using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Hesabyar.App.Resources.Strings;
using Hesabyar.App.Services;
using Hesabyar.Infrastructure.Persistence;
using Hesabyar.Localization.Calendar;
using Microsoft.Extensions.DependencyInjection;

namespace Hesabyar.App.ViewModels;

public sealed class RecurringTransactionsViewModel(
    IServiceScopeFactory scopeFactory,
    IDateCalendarService calendar) : ObservableObject
{
    private bool _isBusy;
    private string _statusMessage = string.Empty;
    public ObservableCollection<RecurringScheduleItemViewModel> Schedules { get; } = [];
    public ObservableCollection<RecurringAccountChoice> Accounts { get; } = [];
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            using var scope = scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<RecurringTransactionService>();
            var schedules = await service.GetSchedulesAsync(cancellationToken);
            var accounts = await service.GetAccountChoicesAsync(cancellationToken);
            Schedules.Clear();
            foreach (var item in schedules)
            {
                var unit = item.IntervalUnit switch
                {
                    "Day" => AppStrings.RecurringDaily,
                    "Week" => AppStrings.RecurringWeekly,
                    "Month" => AppStrings.RecurringMonthly,
                    "Year" => AppStrings.RecurringYearly,
                    _ => item.IntervalUnit
                };
                Schedules.Add(new RecurringScheduleItemViewModel(
                    item.Id, item.Title, item.Kind, MoneyDisplayService.Format(item.AmountMinor, item.CurrencyCode),
                    item.CurrencyCode, calendar.Format(item.NextOccurrenceDate),
                    $"{item.IntervalCount} {unit}", item.IsPaused));
            }
            Accounts.Clear();
            foreach (var account in accounts)
                Accounts.Add(account);
            StatusMessage = Schedules.Count == 0 ? AppStrings.RecurringEmpty : string.Empty;
        }
        catch
        {
            StatusMessage = AppStrings.RecurringLoadFailed;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<IReadOnlyList<RecurringCategoryChoice>> GetCategoriesAsync(string kind, CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<RecurringTransactionService>()
            .GetCategoryChoicesAsync(kind, cancellationToken);
    }

    public async Task<bool> CreateAsync(
        string title, string kind, RecurringAccountChoice account, RecurringCategoryChoice category,
        long amountMinor, DateOnly startDate, DateOnly? endDate, int intervalCount, string intervalUnit)
    {
        using var scope = scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<RecurringTransactionService>();
        try
        {
            await service.CreateAsync(new CreateRecurringScheduleRequest(
                title, kind, account.CurrencyCode, amountMinor, account.Id, category.Id,
                startDate, endDate, intervalCount, intervalUnit));
            await LoadAsync();
            return true;
        }
        catch (ArgumentException)
        {
            StatusMessage = AppStrings.RecurringInvalid;
            return false;
        }
    }

    public async Task<bool> SetPausedAsync(Guid id, bool paused)
    {
        using var scope = scopeFactory.CreateScope();
        try
        {
            var result = await scope.ServiceProvider.GetRequiredService<RecurringTransactionService>()
                .SetPausedAsync(id, paused);
            if (result) await LoadAsync();
            else StatusMessage = AppStrings.RecurringActionFailed;
            return result;
        }
        catch
        {
            StatusMessage = AppStrings.RecurringActionFailed;
            return false;
        }
    }

    public async Task<int> PrepareDueOccurrencesAsync(DateOnly throughDate)
    {
        using var scope = scopeFactory.CreateScope();
        var count = await scope.ServiceProvider.GetRequiredService<RecurringTransactionService>()
            .PrepareDueOccurrencesAsync(throughDate);
        await LoadAsync();
        return count;
    }

    public async Task<IReadOnlyList<RecurringOccurrenceItemViewModel>> GetOccurrencesAsync(Guid scheduleId)
    {
        using var scope = scopeFactory.CreateScope();
        var rows = await scope.ServiceProvider.GetRequiredService<RecurringTransactionService>()
            .GetOccurrencesAsync(scheduleId);
        return rows.Select(x => new RecurringOccurrenceItemViewModel(
            x.Id, calendar.Format(x.OccurrenceDate), x.OccurrenceDate, x.Status, x.TransactionId)).ToArray();
    }

    public async Task<bool> GenerateTransactionAsync(Guid occurrenceId)
    {
        using var scope = scopeFactory.CreateScope();
        try
        {
            var id = await scope.ServiceProvider.GetRequiredService<RecurringTransactionService>()
                .GenerateTransactionAsync(occurrenceId);
            if (id is null)
                StatusMessage = AppStrings.RecurringActionFailed;
            return id is not null;
        }
        catch
        {
            StatusMessage = AppStrings.RecurringActionFailed;
            return false;
        }
    }
}

public sealed record RecurringScheduleItemViewModel(
    Guid Id, string Title, string Kind, string AmountText, string CurrencyCode,
    string NextDateText, string IntervalText, bool IsPaused);

public sealed record RecurringOccurrenceItemViewModel(
    Guid Id, string DateText, DateOnly Date, string Status, Guid? TransactionId);
