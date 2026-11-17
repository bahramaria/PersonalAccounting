using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Hesabyar.Application.Accounts;
using Hesabyar.Application.People;
using Hesabyar.App.Resources.Strings;
using Hesabyar.App.Services;
using Hesabyar.Domain.Accounts;
using Hesabyar.Localization.Calendar;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace Hesabyar.App.ViewModels;

public sealed class PeopleViewModel(IServiceScopeFactory scopeFactory, IDateCalendarService calendar) : ObservableObject
{
    private bool _isBusy;
    private string _statusMessage = string.Empty;
    public ObservableCollection<PersonListItem> People { get; } = [];
    public ObservableCollection<PersonAccountChoice> Accounts { get; } = [];
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            using var scope = scopeFactory.CreateScope();
            var people = scope.ServiceProvider.GetRequiredService<PersonService>();
            var accountService = scope.ServiceProvider.GetRequiredService<AccountService>();
            var rows = await people.GetPeopleAsync(cancellationToken: cancellationToken);
            var accounts = await accountService.GetAccountsAsync(cancellationToken: cancellationToken);
            People.Clear();
            foreach (var person in rows)
            {
                var dues = await people.GetOutstandingDuesAsync(person.Id, cancellationToken, person.CurrencyCode);
                var nextDue = dues.FirstOrDefault(x => x.DueDate <= DateOnly.FromDateTime(DateTime.Today.AddDays(7)));
                var dueText = nextDue is null ? string.Empty :
                    string.Format(AppStrings.PersonDueReminder, calendar.Format(nextDue.DueDate),
                        MoneyDisplayService.Format(nextDue.RemainingAmountMinor, person.CurrencyCode));
                People.Add(new PersonListItem(person.Id, person.Name, person.Phone ?? string.Empty,
                    MoneyDisplayService.Format(person.BalanceMinor == long.MinValue ? long.MaxValue : Math.Abs(person.BalanceMinor), person.CurrencyCode),
                    person.BalanceMinor, person.CurrencyCode, dueText));
            }
            Accounts.Clear();
            if (accounts.IsSuccess && accounts.Value is not null)
                foreach (var account in accounts.Value.Where(x => !x.Account.IsArchived &&
                    x.Account.Type == AccountType.Asset &&
                    x.Account.Kind is AccountKind.Cash or AccountKind.Bank or AccountKind.Card or AccountKind.Wallet))
                    Accounts.Add(new PersonAccountChoice(account.Account.Id, account.Account.Name, account.Account.CurrencyCode));
            StatusMessage = People.Count == 0 ? AppStrings.PeopleEmpty : string.Empty;
        }
        catch { StatusMessage = AppStrings.PeopleLoadFailed; }
        finally { IsBusy = false; }
    }

    public async Task<bool> CreateAsync(string name, string? phone, string? note, CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<PersonService>();
        var result = await service.CreateAsync(new CreatePersonRequest(name, phone, note), cancellationToken);
        if (!result.IsSuccess) { StatusMessage = result.Error.Message; return false; }
        await LoadAsync(cancellationToken);
        return true;
    }

    public async Task<bool> UpdateAsync(Guid id, string name, string? phone, string? note, CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<PersonService>();
        var result = await service.UpdateAsync(id, name, phone, note, cancellationToken);
        if (!result.IsSuccess) { StatusMessage = result.Error.Message; return false; }
        await LoadAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ArchiveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<PersonService>();
        var result = await service.ArchiveAsync(id, cancellationToken);
        if (!result.IsSuccess) { StatusMessage = result.Error.Message; return false; }
        await LoadAsync(cancellationToken);
        return true;
    }

    public async Task<bool> AddDebtAsync(Guid personId, Guid accountId, long amount, bool theyOweMe, string? note, DateOnly? dueDate = null, CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<PersonService>();
        var result = await service.AddDebtAsync(personId, accountId, amount, theyOweMe, DateOnly.FromDateTime(DateTime.Today), note, dueDate, cancellationToken);
        if (!result.IsSuccess) { StatusMessage = result.Error.Message; return false; }
        await LoadAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RepayAsync(Guid personId, Guid accountId, long amount, string? note, bool allowOverpayment = false, CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<PersonService>();
        var result = await service.RepayAsync(personId, accountId, amount, DateOnly.FromDateTime(DateTime.Today), note, allowOverpayment, cancellationToken);
        if (!result.IsSuccess) { StatusMessage = result.Error.Message; return false; }
        await LoadAsync(cancellationToken);
        return true;
    }

    public async Task<string> GetHistoryTextAsync(PersonListItem person, CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<PersonService>();
        var entries = await service.GetEntriesAsync(person.Id, cancellationToken, person.CurrencyCode);
        if (entries.Count == 0) return AppStrings.PersonHistoryEmpty;
        return string.Join(Environment.NewLine, entries.Select(x =>
            $"{calendar.Format(x.Date)}  {x.Kind}  {MoneyDisplayService.Format(x.AmountMinor, x.CurrencyCode)}  {x.AccountName}" +
            (x.DueDate is DateOnly due ? $"  {AppStrings.PersonDueDate}: {calendar.Format(due)}" : string.Empty)));
    }

    public async Task ShareSummaryAsync(PersonListItem person, CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<PersonService>();
        var entries = await service.GetEntriesAsync(person.Id, cancellationToken, person.CurrencyCode);
        var lines = entries.Select(x => $"{calendar.Format(x.Date)} | {x.Kind} | {MoneyDisplayService.Format(x.AmountMinor, x.CurrencyCode)} | {x.AccountName} | {x.Note}" +
            (x.DueDate is DateOnly due ? $" | {AppStrings.PersonDueDate}: {calendar.Format(due)}" : string.Empty));
        var statement = $"{person.Name}\n{person.BalanceDescription}: {person.BalanceText}\n\n{string.Join(Environment.NewLine, lines)}";
        await Share.Default.RequestAsync(new ShareTextRequest { Title = AppStrings.PersonStatementTitle, Text = statement });
    }
}

public sealed record PersonListItem(Guid Id, string Name, string Phone, string BalanceText, long BalanceMinor, string CurrencyCode, string DueReminderText)
{
    public string BalanceDescription => BalanceMinor > 0 ? AppStrings.PersonTheyOweMe :
        BalanceMinor < 0 ? AppStrings.PersonIOweThem : AppStrings.PersonSettled;
}
public sealed record PersonAccountChoice(Guid Id, string Name, string CurrencyCode)
{
    public string DisplayName => $"{Name} ({CurrencyCode})";
}
