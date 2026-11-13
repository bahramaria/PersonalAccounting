using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Hesabyar.Application.Accounts;
using Hesabyar.Application.Transactions;
using Hesabyar.App.Resources.Strings;
using Hesabyar.Domain.Accounts;
using Hesabyar.Domain.Ledger;
using Hesabyar.Localization.Calendar;
using Microsoft.Extensions.DependencyInjection;

namespace Hesabyar.App.ViewModels;

public sealed class TransactionsViewModel(IServiceScopeFactory scopeFactory, ICalendarService calendar) : ObservableObject
{
    private bool _isBusy;
    private string _statusMessage = string.Empty;
    public ObservableCollection<TransactionListItem> Transactions { get; } = [];
    public IReadOnlyList<TransactionChoice> Accounts { get; private set; } = [];
    public IReadOnlyList<TransactionChoice> ExpenseCategories { get; private set; } = [];
    public IReadOnlyList<TransactionChoice> IncomeCategories { get; private set; } = [];
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }

    public async Task LoadAsync(string? search = null, CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            using var scope = scopeFactory.CreateScope();
            var accountResult = await scope.ServiceProvider.GetRequiredService<AccountService>()
                .GetAccountsAsync(includeArchived: true, cancellationToken: cancellationToken);
            if (!accountResult.IsSuccess || accountResult.Value is null)
            {
                StatusMessage = AppStrings.TransactionLoadFailed;
                return;
            }

            var allChoices = accountResult.Value.Select(x => new TransactionChoice(x.Account.Id, x.Account.Name, x.Account.Type)).ToArray();
            var active = accountResult.Value.Where(x => !x.Account.IsArchived).ToArray();
            Accounts = active.Where(x => x.Account.Kind is AccountKind.Cash or AccountKind.Bank or AccountKind.Card or AccountKind.Wallet or AccountKind.CreditCard)
                .Select(x => new TransactionChoice(x.Account.Id, x.Account.Name, x.Account.Type)).ToArray();
            ExpenseCategories = active.Where(x => x.Account.Kind == AccountKind.Category && x.Account.Type == AccountType.Expense)
                .Select(x => new TransactionChoice(x.Account.Id, x.Account.Name, x.Account.Type)).ToArray();
            IncomeCategories = active.Where(x => x.Account.Kind == AccountKind.Category && x.Account.Type == AccountType.Income)
                .Select(x => new TransactionChoice(x.Account.Id, x.Account.Name, x.Account.Type)).ToArray();

            var snapshots = await scope.ServiceProvider.GetRequiredService<TransactionService>()
                .GetTransactionsAsync(search, cancellationToken: cancellationToken);
            Transactions.Clear();
            foreach (var item in snapshots)
            {
                var accountName = allChoices.FirstOrDefault(x => x.Id == item.AccountId)?.Name ?? AppStrings.UnknownAccount;
                var secondName = item.CounterpartyAccountId is Guid secondId
                    ? allChoices.FirstOrDefault(x => x.Id == secondId)?.Name ?? AppStrings.UnknownAccount
                    : string.Empty;
                var persianDate = calendar.ToPersian(item.BusinessDate);
                var dateText = $"{persianDate.Year:0000}/{persianDate.Month:00}/{persianDate.Day:00}";
                Transactions.Add(new TransactionListItem(item.Id, item.Kind, item.BusinessDate, item.AmountMinor,
                    item.AccountId, item.CounterpartyAccountId, item.Description, accountName, secondName,
                    item.AmountMinor.ToString("N0", System.Globalization.CultureInfo.CurrentCulture), dateText));
            }
            StatusMessage = Transactions.Count == 0 ? AppStrings.TransactionsEmpty : string.Empty;
        }
        catch (Exception) { StatusMessage = AppStrings.TransactionLoadFailed; }
        finally { IsBusy = false; }
    }

    public async Task<bool> SaveAsync(Guid? id, TransactionKind kind, DateOnly date, long amount,
        Guid accountId, Guid? secondaryId, string? description, CancellationToken cancellationToken = default)
    {
        if (IsBusy) return false;
        IsBusy = true;
        try
        {
            using var scope = scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<TransactionService>();
            var expense = kind == TransactionKind.Expense && secondaryId.HasValue
                ? new ExpenseRequest(date, amount, accountId, secondaryId.Value, description) : null;
            var income = kind == TransactionKind.Income && secondaryId.HasValue
                ? new IncomeRequest(date, amount, accountId, secondaryId.Value, description) : null;
            var transfer = kind == TransactionKind.Transfer && secondaryId.HasValue
                ? new TransferRequest(date, amount, accountId, secondaryId.Value, description) : null;
            var result = id is Guid existingId
                ? await service.UpdateAsync(existingId, expense, income, transfer, cancellationToken)
                : kind switch
                {
                    TransactionKind.Expense when expense is not null => await service.CreateExpenseAsync(expense, cancellationToken),
                    TransactionKind.Income when income is not null => await service.CreateIncomeAsync(income, cancellationToken),
                    TransactionKind.Transfer when transfer is not null => await service.CreateTransferAsync(transfer, cancellationToken),
                    _ => Hesabyar.Application.Common.Result<Guid>.Failure("transaction.input.invalid", "Transaction details are incomplete.")
                };
            if (!result.IsSuccess) { StatusMessage = result.Error.Message ?? AppStrings.TransactionCreateFailed; return false; }
        }
        catch (Exception) { StatusMessage = AppStrings.TransactionCreateFailed; return false; }
        finally { IsBusy = false; }
        await LoadAsync(cancellationToken: cancellationToken);
        return true;
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        RunAsync(async service => (await service.DeleteAsync(id, cancellationToken)).IsSuccess, cancellationToken);

    public Task<bool> DuplicateAsync(Guid id, CancellationToken cancellationToken = default) =>
        RunAsync(async service => (await service.DuplicateAsync(id, cancellationToken)).IsSuccess, cancellationToken);

    private async Task<bool> RunAsync(Func<TransactionService, Task<bool>> action, CancellationToken cancellationToken)
    {
        if (IsBusy) return false;
        IsBusy = true;
        try
        {
            using var scope = scopeFactory.CreateScope();
            if (!await action(scope.ServiceProvider.GetRequiredService<TransactionService>()))
            {
                StatusMessage = AppStrings.TransactionCreateFailed;
                return false;
            }
        }
        catch (Exception) { StatusMessage = AppStrings.TransactionCreateFailed; return false; }
        finally { IsBusy = false; }
        await LoadAsync(cancellationToken: cancellationToken);
        return true;
    }
}

public sealed record TransactionChoice(Guid Id, string Name, AccountType Type);
public sealed record TransactionListItem(Guid Id, TransactionKind Kind, DateOnly BusinessDate, long AmountMinor,
    Guid AccountId, Guid? SecondaryId, string? Description, string AccountName, string SecondaryName, string AmountText, string DateText)
{
    public string KindName => Kind switch
    {
        TransactionKind.Expense => AppStrings.Expense,
        TransactionKind.Income => AppStrings.Income,
        TransactionKind.Transfer => AppStrings.Transfer,
        _ => string.Empty
    };
    public string DetailText => string.IsNullOrWhiteSpace(Description)
        ? $"{AccountName} • {SecondaryName}" : $"{AccountName} • {SecondaryName} • {Description}";
}
