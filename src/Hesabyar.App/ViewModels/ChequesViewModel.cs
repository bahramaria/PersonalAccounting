using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Hesabyar.Application.Accounts;
using Hesabyar.Application.Cheques;
using Hesabyar.Application.People;
using Hesabyar.App.Resources.Strings;
using Hesabyar.App.Services;
using Hesabyar.Domain.Accounts;
using Hesabyar.Localization.Calendar;
using Microsoft.Extensions.DependencyInjection;

namespace Hesabyar.App.ViewModels;

public sealed class ChequesViewModel(IServiceScopeFactory scopeFactory, IDateCalendarService calendar) : ObservableObject
{
    private string _statusMessage = string.Empty;
    private IReadOnlyList<string> _currencyCodes = ["IRR"];
    public ObservableCollection<ChequeListItem> Cheques { get; } = [];
    public ObservableCollection<ChequeChoice> People { get; } = [];
    public ObservableCollection<ChequeChoice> Accounts { get; } = [];
    public IReadOnlyList<string> CurrencyCodes => _currencyCodes;
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var provider = scope.ServiceProvider;
            var service = provider.GetRequiredService<ChequeService>();
            var peopleService = provider.GetRequiredService<PersonService>();
            var accountService = provider.GetRequiredService<AccountService>();
            var result = await service.GetAsync(includeArchived: true, cancellationToken: cancellationToken);
            var people = await peopleService.GetPeopleAsync(cancellationToken: cancellationToken);
            var accounts = await accountService.GetAccountsAsync(cancellationToken: cancellationToken);
            if (!result.IsSuccess || result.Value is null) throw new InvalidOperationException();
            Cheques.Clear();
            foreach (var cheque in result.Value)
                Cheques.Add(new ChequeListItem(cheque.Id, cheque.Direction, cheque.BankName, cheque.SerialNumber,
                    cheque.SayadNumber, calendar.Format(cheque.DueDate), cheque.DueDate,
                    MoneyDisplayService.Format(cheque.AmountMinor, cheque.CurrencyCode), cheque.AmountMinor, cheque.PersonName ?? string.Empty,
                    StatusText(cheque.Status), cheque.Status, cheque.Note, cheque.PersonId, cheque.IsArchived,
                    ReminderText(cheque.DueDate, cheque.Status), cheque.CurrencyCode));
            People.Clear();
            foreach (var person in people.Where(x => !x.IsArchived).GroupBy(x => x.Id).Select(x => x.First()))
                People.Add(new ChequeChoice(person.Id, person.Name));
            Accounts.Clear();
            if (accounts.IsSuccess && accounts.Value is not null)
                foreach (var account in accounts.Value.Where(x => !x.Account.IsArchived &&
                    x.Account.Type == AccountType.Asset &&
                    x.Account.Kind is AccountKind.Cash or AccountKind.Bank or AccountKind.Card or AccountKind.Wallet))
                    Accounts.Add(new ChequeChoice(account.Account.Id, account.Account.Name, account.Account.CurrencyCode));
            _currencyCodes = Accounts.Select(x => x.CurrencyCode).Where(x => x is not null)
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).Cast<string>().ToArray();
            if (_currencyCodes.Count == 0) _currencyCodes = ["IRR"];
            StatusMessage = string.Empty;
        }
        catch { StatusMessage = AppStrings.ChequeLoadFailed; }
    }

    public async Task<bool> CreateAsync(CreateChequeRequest request, CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<ChequeService>().CreateAsync(request, cancellationToken);
        if (!result.IsSuccess) { StatusMessage = result.Error.Message; return false; }
        await LoadAsync(cancellationToken); return true;
    }

    public async Task<bool> UpdateAsync(Guid id, UpdateChequeRequest request, CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<ChequeService>().UpdateAsync(id, request, cancellationToken);
        if (!result.IsSuccess) { StatusMessage = result.Error.Message; return false; }
        await LoadAsync(cancellationToken); return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<ChequeService>().DeleteAsync(id, cancellationToken);
        if (!result.IsSuccess) { StatusMessage = result.Error.Message; return false; }
        await LoadAsync(cancellationToken); return true;
    }

    public async Task<bool> ChangeStatusAsync(Guid id, ChequeStatus status, Guid? accountId, CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<ChequeService>().ChangeStatusAsync(id, status, accountId, cancellationToken);
        if (!result.IsSuccess) { StatusMessage = result.Error.Message; return false; }
        await LoadAsync(cancellationToken); return true;
    }

    public string GetCalendarText(int daysAhead = 30)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var lines = new List<string>();
        for (var offset = 0; offset <= daysAhead; offset++)
        {
            var date = today.AddDays(offset);
            var due = Cheques.Where(x => x.DueDate == date && x.Status is ChequeStatus.Pending or ChequeStatus.Returned).ToArray();
            if (due.Length == 0) continue;
            lines.Add(calendar.Format(date));
            lines.AddRange(due.Select(x => $"  {x.BankName} · {x.SerialNumber} · {x.AmountText}"));
        }
        return lines.Count == 0 ? AppStrings.ChequeCalendarEmpty : string.Join(Environment.NewLine, lines);
    }

    private static string ReminderText(DateOnly dueDate, ChequeStatus status)
    {
        if (status is not (ChequeStatus.Pending or ChequeStatus.Returned)) return string.Empty;
        var days = dueDate.DayNumber - DateOnly.FromDateTime(DateTime.Today).DayNumber;
        return days switch { 0 => AppStrings.ChequeDueToday, 1 => AppStrings.ChequeDueInOneDay, 3 => AppStrings.ChequeDueInThreeDays, _ => string.Empty };
    }

    private static string StatusText(ChequeStatus status) => status switch
    {
        ChequeStatus.Pending => AppStrings.ChequePending,
        ChequeStatus.Cleared => AppStrings.ChequeCleared,
        ChequeStatus.Returned => AppStrings.ChequeReturned,
        ChequeStatus.Spent => AppStrings.ChequeSpent,
        ChequeStatus.Voided => AppStrings.ChequeVoided,
        _ => status.ToString()
    };
}

public sealed record ChequeChoice(Guid Id, string Name, string? CurrencyCode = null)
{
    public string DisplayName => CurrencyCode is null ? Name : $"{Name} ({CurrencyCode})";
}
public sealed record ChequeListItem(Guid Id, ChequeDirection Direction, string BankName, string SerialNumber,
    string? SayadNumber, string DueDateText, DateOnly DueDate, string AmountText, long AmountMinor,
    string PersonName, string StatusText, ChequeStatus Status, string? Note, Guid? PersonId, bool IsArchived, string ReminderText,
    string CurrencyCode)
{
    public string DirectionText => Direction == ChequeDirection.Received ? AppStrings.ChequeReceived : AppStrings.ChequeIssued;
}
