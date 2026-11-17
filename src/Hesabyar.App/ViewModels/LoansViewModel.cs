using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Hesabyar.Application.Accounts;
using Hesabyar.Application.Loans;
using Hesabyar.Application.People;
using Hesabyar.App.Resources.Strings;
using Hesabyar.App.Services;
using Hesabyar.Domain.Accounts;
using Hesabyar.Localization.Calendar;
using Microsoft.Extensions.DependencyInjection;

namespace Hesabyar.App.ViewModels;

public sealed class LoansViewModel(IServiceScopeFactory scopeFactory, IDateCalendarService calendar) : ObservableObject
{
    private string _statusMessage = string.Empty;
    public ObservableCollection<LoanListItem> Loans { get; } = [];
    public ObservableCollection<LoanChoice> Accounts { get; } = [];
    public ObservableCollection<LoanChoice> People { get; } = [];
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var provider = scope.ServiceProvider;
            var loans = await provider.GetRequiredService<LoanService>().GetLoansAsync(cancellationToken);
            var accounts = await provider.GetRequiredService<AccountService>().GetAccountsAsync(cancellationToken: cancellationToken);
            var people = await provider.GetRequiredService<PersonService>().GetPeopleAsync(cancellationToken: cancellationToken);
            Loans.Clear();
            var today = DateOnly.FromDateTime(DateTime.Today);
            foreach (var loan in loans)
            {
                var next = loan.NextInstallment;
                var overdue = next is null || next.DueDate >= today ? 0 : today.DayNumber - next.DueDate.DayNumber;
                Loans.Add(new LoanListItem(loan.Id, loan.Name,
                    loan.Direction == LoanDirection.Given ? AppStrings.LoanGiven : AppStrings.LoanReceived,
                    loan.AccountName, loan.PersonName ?? string.Empty, MoneyDisplayService.Format(loan.PrincipalMinor, loan.CurrencyCode),
                    MoneyDisplayService.Format(loan.RemainingMinor, loan.CurrencyCode), loan.RemainingMinor, loan.PrincipalMinor,
                    next is null ? string.Empty : calendar.Format(next.DueDate), next?.DueDate,
                    next is null ? string.Empty : MoneyDisplayService.Format(next.RemainingMinor, loan.CurrencyCode),
                    next?.RemainingMinor ?? 0, overdue, loan.Installments.Count, loan.IsClosed, loan));
            }
            Accounts.Clear();
            if (accounts.IsSuccess && accounts.Value is not null)
                foreach (var account in accounts.Value.Where(x => !x.Account.IsArchived &&
                    x.Account.Type == AccountType.Asset &&
                    x.Account.Kind is AccountKind.Cash or AccountKind.Bank or AccountKind.Card or AccountKind.Wallet))
                    Accounts.Add(new LoanChoice(account.Account.Id, account.Account.Name, account.Account.CurrencyCode));
            People.Clear();
            foreach (var person in people.Where(x => !x.IsArchived))
                People.Add(new LoanChoice(person.Id, person.Name));
            StatusMessage = Loans.Count == 0 ? AppStrings.LoansEmpty : string.Empty;
        }
        catch { StatusMessage = AppStrings.LoansLoadFailed; }
    }

    public async Task<bool> CreateAsync(CreateLoanRequest request, CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<LoanService>().CreateAsync(request, cancellationToken);
        if (!result.IsSuccess) { StatusMessage = AppStrings.LoanSaveFailed; return false; }
        await LoadAsync(cancellationToken); return true;
    }

    public async Task<bool> PayAsync(Guid loanId, Guid accountId, long amount, DateOnly date,
        CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<LoanService>()
            .PayAsync(loanId, accountId, amount, date, cancellationToken);
        if (!result.IsSuccess) { StatusMessage = AppStrings.LoanPaymentFailed; return false; }
        await LoadAsync(cancellationToken); return true;
    }

    public string GetScheduleText(LoanListItem item)
    {
        var rows = item.Loan.Installments.OrderBy(x => x.DueDate).ThenBy(x => x.Sequence)
            .Select(x => $"{calendar.Format(x.DueDate)}  {MoneyDisplayService.Format(x.RemainingMinor, item.Loan.CurrencyCode)}  " +
                $"{AppStrings.LoanInstallmentNumber} {x.Sequence}");
        return string.Join(Environment.NewLine, rows);
    }
}

public sealed record LoanChoice(Guid Id, string Name, string CurrencyCode)
{
    public string DisplayName => $"{Name} ({CurrencyCode})";
}
public sealed record LoanListItem(Guid Id, string Name, string DirectionText, string AccountName, string PersonName,
    string PrincipalText, string RemainingText, long RemainingMinor, long PrincipalMinor,
    string NextDueText, DateOnly? NextDueDate, string NextAmountText, long NextAmountMinor,
    int OverdueDays, int InstallmentCount, bool IsClosed, LoanSnapshot Loan)
{
    public string StateText => IsClosed ? AppStrings.LoanClosed :
        OverdueDays > 0 ? string.Format(AppStrings.LoanOverdueDays, OverdueDays) :
        NextDueDate is null ? AppStrings.LoanSettled : AppStrings.LoanNextDue;
}
