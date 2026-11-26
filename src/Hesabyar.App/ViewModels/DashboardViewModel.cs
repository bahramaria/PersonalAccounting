using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Hesabyar.Application.Accounts;
using Hesabyar.Application.Transactions;
using Hesabyar.Application.People;
using Hesabyar.Application.Loans;
using Hesabyar.App.Resources.Strings;
using Hesabyar.App.Services;
using Hesabyar.Domain.Accounts;
using Hesabyar.Domain.Ledger;
using Hesabyar.Infrastructure.Persistence;
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
    private string _currencyCode = "IRR";
    private IReadOnlyList<string> _currencyCodes = ["IRR"];

    public ObservableCollection<DashboardTransactionItem> RecentTransactions { get; } = [];
    public IReadOnlyList<string> CurrencyCodes => _currencyCodes;
    public string CurrencyCode => _currencyCode;

    public void SelectCurrency(string currencyCode)
    {
        if (!_currencyCodes.Contains(currencyCode, StringComparer.OrdinalIgnoreCase))
            return;
        _currencyCode = currencyCode;
        OnPropertyChanged(nameof(CurrencyCode));
    }
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

            var physicalAssets = await scope.ServiceProvider.GetRequiredService<PhysicalAssetService>()
                .GetAsync(cancellationToken: cancellationToken);
            _currencyCodes = accountResult.Value.Select(x => x.Account.CurrencyCode)
                .Concat(physicalAssets.Select(x => x.CurrencyCode))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray();
            if (!_currencyCodes.Contains(_currencyCode, StringComparer.OrdinalIgnoreCase))
                _currencyCode = _currencyCodes.FirstOrDefault() ?? "IRR";
            OnPropertyChanged(nameof(CurrencyCode));

            var included = accountResult.Value.Where(x =>
                x.Account.IncludeInTotal && x.Account.CurrencyCode == _currencyCode).ToArray();
            var people = await scope.ServiceProvider.GetRequiredService<PersonService>()
                .GetPeopleAsync(cancellationToken: cancellationToken);
            people = people.Where(x => x.CurrencyCode == _currencyCode).ToArray();
            var loans = (await scope.ServiceProvider.GetRequiredService<LoanService>()
                .GetLoansAsync(cancellationToken)).Where(x => x.CurrencyCode == _currencyCode).ToArray();
            var assets = included.Where(x => x.Account.Type == AccountType.Asset).Sum(x => (decimal)x.BalanceMinor)
                + people.Where(x => x.BalanceMinor > 0).Sum(x => (decimal)x.BalanceMinor)
                + loans.Where(x => x.Direction == LoanDirection.Given).Sum(x => (decimal)x.RemainingPrincipalMinor)
                + physicalAssets.Where(x => x.CurrencyCode == _currencyCode)
                    .Sum(x => (decimal)(x.CurrentValueMinor ?? x.AcquisitionCostMinor));
            var liabilities = included.Where(x => x.Account.Type == AccountType.Liability).Sum(x => Math.Abs((decimal)x.BalanceMinor))
                + people.Where(x => x.BalanceMinor < 0).Sum(x => Math.Abs((decimal)x.BalanceMinor))
                + loans.Where(x => x.Direction == LoanDirection.Received).Sum(x => (decimal)x.RemainingMinor);
            AssetsMessage = FormatMoney(assets, _currencyCode);
            LiabilitiesMessage = FormatMoney(liabilities, _currencyCode);
            NetWorthMessage = FormatMoney(assets - liabilities, _currencyCode);

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
                    TransactionKind.CurrencyExchange => AppStrings.CurrencyExchange,
                    _ => string.Empty
                };
                var description = string.IsNullOrWhiteSpace(item.Description)
                    ? names.GetValueOrDefault(item.AccountId, AppStrings.UnknownAccount)
                    : item.Description;
                var dateText = calendar.Format(item.BusinessDate);
                var amountText = item.Kind == TransactionKind.CurrencyExchange && item.CounterAmountMinor is long counterAmount
                    ? $"{MoneyDisplayService.Format(item.AmountMinor, item.CurrencyCode ?? "IRR")} → {MoneyDisplayService.Format(counterAmount, item.CounterCurrencyCode ?? "IRR")}"
                    : MoneyDisplayService.Format(item.AmountMinor, item.CurrencyCode ?? "IRR");
                RecentTransactions.Add(new DashboardTransactionItem(
                    kind, description!, amountText, dateText));
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

    private static string FormatMoney(decimal amount, string currencyCode) =>
        MoneyDisplayService.Format(decimal.ToInt64(amount), currencyCode);
}

public sealed record DashboardTransactionItem(string Kind, string Description, string AmountText, string DateText);
