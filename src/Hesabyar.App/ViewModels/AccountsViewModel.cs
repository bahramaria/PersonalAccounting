using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Hesabyar.Application.Accounts;
using Hesabyar.App.Resources.Strings;
using Hesabyar.Domain.Accounts;
using Hesabyar.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace Hesabyar.App.ViewModels;

public sealed class AccountsViewModel : ObservableObject
{
    private readonly IServiceScopeFactory _scopeFactory;
    private bool _isBusy;
    private string _statusMessage = string.Empty;

    public AccountsViewModel(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public ObservableCollection<AccountListItem> Accounts { get; } = [];

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy)
            return;

        IsBusy = true;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<AccountService>();
            var result = await service.GetAccountsAsync(cancellationToken: cancellationToken);
            if (!result.IsSuccess || result.Value is null)
            {
                StatusMessage = AppStrings.AccountsLoadFailed;
                return;
            }

            Accounts.Clear();
            foreach (var item in result.Value.Where(x =>
                         x.Account.Kind is AccountKind.Cash or AccountKind.Bank or AccountKind.Card
                             or AccountKind.Wallet or AccountKind.CreditCard))
            {
                Accounts.Add(new AccountListItem(
                    item.Account.Id,
                    item.Account.Name,
                    GetKindName(item.Account.Kind),
                    FormatBalance(item.BalanceMinor)));
            }

            StatusMessage = Accounts.Count == 0 ? AppStrings.AccountsEmpty : string.Empty;
        }
        catch (Exception)
        {
            StatusMessage = AppStrings.AccountsLoadFailed;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> CreateAsync(
        string name,
        AccountKind kind,
        long openingBalanceMinor,
        CancellationToken cancellationToken = default)
    {
        if (IsBusy)
            return false;

        IsBusy = true;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<AccountService>();
            var request = new CreateAccountRequest(
                name,
                kind == AccountKind.CreditCard ? AccountType.Liability : AccountType.Asset,
                kind,
                CurrencyCode: "IRR",
                OpeningBalanceMinor: openingBalanceMinor,
                OpeningDate: openingBalanceMinor > 0 ? DateOnly.FromDateTime(DateTime.Today) : null);

            var result = await service.CreateAsync(request, cancellationToken);
            if (!result.IsSuccess)
            {
                StatusMessage = result.Error.Message ?? AppStrings.AccountCreateFailed;
                return false;
            }

            await LoadAfterCreateAsync(cancellationToken);
            return true;
        }
        catch (Exception)
        {
            StatusMessage = AppStrings.AccountCreateFailed;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> RenameAsync(Guid accountId, string name, CancellationToken cancellationToken = default)
    {
        if (IsBusy)
            return false;

        IsBusy = true;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<AccountService>();
            var result = await service.RenameAsync(accountId, name, cancellationToken);
            if (!result.IsSuccess)
            {
                StatusMessage = result.Error.Message ?? AppStrings.AccountUpdateFailed;
                return false;
            }

            await ReloadAccountsAsync(cancellationToken);
            return true;
        }
        catch (Exception)
        {
            StatusMessage = AppStrings.AccountUpdateFailed;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> ArchiveAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        if (IsBusy)
            return false;

        IsBusy = true;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<AccountService>();
            var result = await service.ArchiveAsync(accountId, cancellationToken);
            if (!result.IsSuccess)
            {
                StatusMessage = result.Error.Message ?? AppStrings.AccountArchiveFailed;
                return false;
            }

            await ReloadAccountsAsync(cancellationToken);
            return true;
        }
        catch (Exception)
        {
            StatusMessage = AppStrings.AccountArchiveFailed;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ReloadAccountsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AccountService>();
        var result = await service.GetAccountsAsync(cancellationToken: cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            StatusMessage = AppStrings.AccountsLoadFailed;
            return;
        }

        Accounts.Clear();
        foreach (var item in result.Value.Where(x =>
                     x.Account.Kind is AccountKind.Cash or AccountKind.Bank or AccountKind.Card
                         or AccountKind.Wallet or AccountKind.CreditCard))
        {
            Accounts.Add(new AccountListItem(
                item.Account.Id,
                item.Account.Name,
                GetKindName(item.Account.Kind),
                FormatBalance(item.BalanceMinor)));
        }

        StatusMessage = Accounts.Count == 0 ? AppStrings.AccountsEmpty : string.Empty;
    }

    private async Task LoadAfterCreateAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AccountService>();
        var result = await service.GetAccountsAsync(cancellationToken: cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            StatusMessage = AppStrings.AccountsLoadFailed;
            return;
        }

        Accounts.Clear();
        foreach (var item in result.Value.Where(x =>
                     x.Account.Kind is AccountKind.Cash or AccountKind.Bank or AccountKind.Card
                         or AccountKind.Wallet or AccountKind.CreditCard))
        {
            Accounts.Add(new AccountListItem(
                item.Account.Id,
                item.Account.Name,
                GetKindName(item.Account.Kind),
                FormatBalance(item.BalanceMinor)));
        }

        StatusMessage = Accounts.Count == 0 ? AppStrings.AccountsEmpty : string.Empty;
    }

    private static string FormatBalance(long balanceMinor)
    {
        return MoneyDisplayService.Format(balanceMinor);
    }

    private static string GetKindName(AccountKind kind) => kind switch
    {
        AccountKind.Cash => AppStrings.AccountKindCash,
        AccountKind.Bank => AppStrings.AccountKindBank,
        AccountKind.Card => AppStrings.AccountKindCard,
        AccountKind.Wallet => AppStrings.AccountKindWallet,
        AccountKind.CreditCard => AppStrings.AccountKindCreditCard,
        _ => string.Empty
    };
}

public sealed record AccountListItem(Guid Id, string Name, string KindName, string BalanceText);
