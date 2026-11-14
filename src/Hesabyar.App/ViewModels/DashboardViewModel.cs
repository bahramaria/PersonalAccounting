using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Hesabyar.Application.Accounts;
using Hesabyar.Application.Transactions;
using Hesabyar.App.Resources.Strings;
using Hesabyar.App.Services;
using Hesabyar.Domain.Accounts;
using Hesabyar.Domain.Ledger;
using Hesabyar.Localization;
using Hesabyar.Localization.Calendar;
using Microsoft.Extensions.DependencyInjection;

namespace Hesabyar.App.ViewModels;

public sealed class DashboardViewModel(
    IServiceScopeFactory scopeFactory,
    IDateCalendarService calendar) : ObservableObject
{
    private string _assetsMessage = AppStrings.BalanceUnavailable;
    private string _liabilitiesMessage = AppStrings.BalanceUnavailable;
    private string _netWorthMessage = AppStrings.BalanceUnavailable;
    private string _statusMessage = string.Empty;
    private bool _isBusy;

    public ObservableCollection<DashboardTransactionItem> RecentTransactions { get; } = [];
    public string AssetsMessage { get => _assetsMessage; private set => SetProperty(ref _assetsMessage, value); }
    public string LiabilitiesMessage { get => _liabilitiesMessage; private set => SetProperty(ref _liabilitiesMessage, value); }
    public string NetWorthMessage { get => _netWorthMessage; private set => SetProperty(ref _netWorthMessage, value); }
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            using var scope = scopeFactory.CreateScope();
            var accountResult = await scope.ServiceProvider.GetRequiredService<AccountService>()
                .GetAccountsAsync(includeArchived: false, cancellationToken);
            if (!accountResult.IsSuccess || accountResult.Value is null)
                throw new InvalidOperationException("Account balances could not be loaded.");

            var included = accountResult.Value.Where(x => x.Account.IncludeInTotal).ToArray();
            var assets = included.Where(x => x.Account.Type == AccountType.Asset).Sum(x => (decimal)x.BalanceMinor);
            var liabilities = included.Where(x => x.Account.Type == AccountType.Liability).Sum(x => Math.Abs((decimal)x.BalanceMinor));
            AssetsMessage = FormatMoney(assets);
            LiabilitiesMessage = FormatMoney(liabilities);
            NetWorthMessage = FormatMoney(assets - liabilities);

            var names = accountResult.Value.ToDictionary(x => x.Account.Id, x => x.Account.Name);
            var transactions = await scope.ServiceProvider.GetRequiredService<TransactionService>()
                .GetTransactionsAsync(take: 5, cancellationToken: cancellationToken);
            RecentTransactions.Clear();
            foreach (var item in transactions)
            {
                var kind = item.Kind switch
                {
                    TransactionKind.Expense => AppStrings.Expense,
                    TransactionKind.Income => AppStrings.Income,
                    TransactionKind.Transfer => AppStrings.Transfer,
                    _ => string.Empty
                };
                var description = string.IsNullOrWhiteSpace(item.Description)
                    ? names.GetValueOrDefault(item.AccountId, AppStrings.UnknownAccount)
                    : item.Description;
                var dateText = calendar.Format(item.BusinessDate);
                RecentTransactions.Add(new DashboardTransactionItem(
                    kind, description!, FormatMoney(item.AmountMinor), dateText));
            }
            StatusMessage = RecentTransactions.Count == 0 ? AppStrings.TransactionsEmpty : string.Empty;
        }
        catch (Exception)
        {
            StatusMessage = AppStrings.DashboardLoadFailed;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string FormatMoney(decimal amount) => MoneyDisplayService.Format(amount);
}

public sealed record DashboardTransactionItem(string Kind, string Description, string AmountText, string DateText);
