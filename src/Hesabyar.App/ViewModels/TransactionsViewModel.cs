using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Hesabyar.Application.Accounts;
using Hesabyar.Application.Transactions;
using Hesabyar.Application.Planning;
using Hesabyar.App.Resources.Strings;
using Hesabyar.Domain.Accounts;
using Hesabyar.Domain.Ledger;
using Hesabyar.Localization.Calendar;
using Microsoft.Extensions.DependencyInjection;

namespace Hesabyar.App.ViewModels;

public sealed class TransactionsViewModel(IServiceScopeFactory scopeFactory, IDateCalendarService calendar) : ObservableObject
{
    private bool _isBusy;
    private string _statusMessage = string.Empty;
    public ObservableCollection<TransactionListItem> Transactions { get; } = [];
    public IReadOnlyList<TransactionChoice> Accounts { get; private set; } = [];
    public IReadOnlyList<TransactionChoice> ExpenseCategories { get; private set; } = [];
    public IReadOnlyList<TransactionChoice> IncomeCategories { get; private set; } = [];
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }

    private const int PageSize = 100;
    private string? _search;
    private DateOnly? _fromDate;
    private DateOnly? _toDate;
    private Guid? _accountId;
    private Guid? _categoryId;
    private long? _minimumAmountMinor;
    private long? _maximumAmountMinor;
    private TransactionSortOrder _sortOrder = TransactionSortOrder.NewestFirst;
    private Dictionary<Guid, string> _accountNames = [];
    private bool _hasMore = true;

    public bool HasMore
    {
        get => _hasMore;
        private set => SetProperty(ref _hasMore, value);
    }

    public async Task LoadAsync(
        string? search = null,
        DateOnly? fromDate = null,
        DateOnly? toDate = null,
        Guid? accountId = null,
        Guid? categoryId = null,
        long? minimumAmountMinor = null,
        long? maximumAmountMinor = null,
        TransactionSortOrder sortOrder = TransactionSortOrder.NewestFirst,
        CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        _search = search;
        _fromDate = fromDate;
        _toDate = toDate;
        _accountId = accountId;
        _categoryId = categoryId;
        _minimumAmountMinor = minimumAmountMinor;
        _maximumAmountMinor = maximumAmountMinor;
        _sortOrder = sortOrder;

        IsBusy = true;
        HasMore = true;
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

            var allChoices = accountResult.Value.Select(x => new TransactionChoice(x.Account.Id, x.Account.Name, x.Account.Type, x.Account.CurrencyCode)).ToArray();
            _accountNames = allChoices.ToDictionary(x => x.Id, x => x.Name);
            var active = accountResult.Value.Where(x => !x.Account.IsArchived).ToArray();
            Accounts = active.Where(x => x.Account.Kind is AccountKind.Cash or AccountKind.Bank or AccountKind.Card or AccountKind.Wallet or AccountKind.CreditCard)
                .Select(x => new TransactionChoice(x.Account.Id, x.Account.Name, x.Account.Type)).ToArray();
            ExpenseCategories = active.Where(x => x.Account.Kind == AccountKind.Category && x.Account.Type == AccountType.Expense)
                .Select(x => new TransactionChoice(x.Account.Id, x.Account.Name, x.Account.Type)).ToArray();
            IncomeCategories = active.Where(x => x.Account.Kind == AccountKind.Category && x.Account.Type == AccountType.Income)
                .Select(x => new TransactionChoice(x.Account.Id, x.Account.Name, x.Account.Type)).ToArray();

            Transactions.Clear();
            var snapshots = await scope.ServiceProvider.GetRequiredService<TransactionService>()
                .GetTransactionsAsync(_search, _fromDate, _toDate, _accountId, _categoryId,
                    _minimumAmountMinor, _maximumAmountMinor, _sortOrder,
                    take: PageSize, cancellationToken: cancellationToken, skip: 0);
            AppendTransactions(snapshots);
            HasMore = snapshots.Count == PageSize;
            StatusMessage = Transactions.Count == 0 ? AppStrings.TransactionsEmpty : string.Empty;
        }
        catch (Exception) { StatusMessage = AppStrings.TransactionLoadFailed; }
        finally { IsBusy = false; }
    }

    public async Task LoadMoreAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy || !HasMore) return;
        IsBusy = true;
        try
        {
            using var scope = scopeFactory.CreateScope();
            var snapshots = await scope.ServiceProvider.GetRequiredService<TransactionService>()
                .GetTransactionsAsync(_search, _fromDate, _toDate, _accountId, _categoryId,
                    _minimumAmountMinor, _maximumAmountMinor, _sortOrder,
                    take: PageSize, cancellationToken: cancellationToken, skip: Transactions.Count);
            AppendTransactions(snapshots);
            HasMore = snapshots.Count == PageSize;
        }
        catch (Exception) { StatusMessage = AppStrings.TransactionLoadFailed; }
        finally { IsBusy = false; }
    }

    private void AppendTransactions(IReadOnlyList<TransactionSnapshot> snapshots)
    {
        foreach (var item in snapshots)
        {
            var accountName = _accountNames.GetValueOrDefault(item.AccountId, AppStrings.UnknownAccount);
            var secondName = item.CounterpartyAccountId is Guid secondId
                ? _accountNames.GetValueOrDefault(secondId, AppStrings.UnknownAccount)
                : string.Empty;
            var dateText = calendar.Format(item.BusinessDate);
            Transactions.Add(new TransactionListItem(item.Id, item.Kind, item.BusinessDate, item.AmountMinor,
                item.AccountId, item.CounterpartyAccountId, item.Description, accountName, secondName,
                item.AmountMinor.ToString("N0", System.Globalization.CultureInfo.CurrentCulture), dateText, item.DocumentNumber));
        }
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
        await ReloadCurrentAsync(cancellationToken);
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
        await ReloadCurrentAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<TagSnapshot>> GetTagsAsync(CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<TagService>().GetAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetTransactionTagIdsAsync(Guid transactionId, CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<TagService>().GetForTransactionAsync(transactionId, cancellationToken);
    }

    public async Task<bool> SetTransactionTagsAsync(Guid transactionId, IReadOnlyCollection<Guid> tagIds, CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<TagService>().SetTransactionTagsAsync(transactionId, tagIds, cancellationToken);
        if (!result.IsSuccess) StatusMessage = result.Error.Message ?? AppStrings.TransactionCreateFailed;
        return result.IsSuccess;
    }

    private Task ReloadCurrentAsync(CancellationToken cancellationToken) => LoadAsync(
        _search, _fromDate, _toDate, _accountId, _categoryId,
        _minimumAmountMinor, _maximumAmountMinor, _sortOrder, cancellationToken);
}

public sealed record TransactionChoice(Guid Id, string Name, AccountType Type, string CurrencyCode = "IRR");
public sealed record TransactionListItem(Guid Id, TransactionKind Kind, DateOnly BusinessDate, long AmountMinor,
    Guid AccountId, Guid? SecondaryId, string? Description, string AccountName, string SecondaryName, string AmountText, string DateText, string? DocumentNumber = null)
{
    public string KindName => Kind switch
    {
        TransactionKind.Expense => AppStrings.Expense,
        TransactionKind.Income => AppStrings.Income,
        TransactionKind.Transfer => AppStrings.Transfer,
        TransactionKind.LoanOpening => AppStrings.LoanOpeningTransaction,
        TransactionKind.LoanPayment => AppStrings.LoanPaymentTransaction,
        _ => string.Empty
    };
    public string DetailText
    {
        get
        {
            var detail = string.IsNullOrWhiteSpace(Description)
                ? $"{AccountName} • {SecondaryName}" : $"{AccountName} • {SecondaryName} • {Description}";
            return string.IsNullOrWhiteSpace(DocumentNumber) ? detail : $"{DocumentNumber} • {detail}";
        }
    }
}
