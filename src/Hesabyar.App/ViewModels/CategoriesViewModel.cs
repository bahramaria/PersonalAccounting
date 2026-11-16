using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Hesabyar.Application.Accounts;
using Hesabyar.App.Resources.Strings;
using Hesabyar.Domain.Accounts;
using Microsoft.Extensions.DependencyInjection;

namespace Hesabyar.App.ViewModels;

public sealed class CategoriesViewModel : ObservableObject
{
    private readonly IServiceScopeFactory _scopeFactory;
    private bool _isBusy;
    private string _statusMessage = string.Empty;

    public CategoriesViewModel(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;
    public ObservableCollection<CategoryListItem> Categories { get; } = [];
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        IsBusy = true;
        try { await ReloadAsync(cancellationToken); }
        catch (Exception) { StatusMessage = AppStrings.CategoriesLoadFailed; }
        finally { IsBusy = false; }
    }

    public Task<bool> CreateAsync(string name, AccountType type, string currencyCode = "IRR", CancellationToken cancellationToken = default) =>
        ExecuteAsync(async service =>
        {
            var result = await service.CreateAsync(new CreateAccountRequest(name, type, AccountKind.Category, CurrencyCode: currencyCode), cancellationToken);
            if (!result.IsSuccess) { StatusMessage = result.Error.Message ?? AppStrings.CategoryCreateFailed; return false; }
            await ReloadAsync(cancellationToken);
            return true;
        }, AppStrings.CategoryCreateFailed, cancellationToken);

    public Task<bool> RenameAsync(Guid id, string name, CancellationToken cancellationToken = default) =>
        ExecuteAsync(async service =>
        {
            var result = await service.RenameAsync(id, name, cancellationToken);
            if (!result.IsSuccess) { StatusMessage = result.Error.Message ?? AppStrings.CategoryUpdateFailed; return false; }
            await ReloadAsync(cancellationToken);
            return true;
        }, AppStrings.CategoryUpdateFailed, cancellationToken);

    public Task<bool> ArchiveAsync(Guid id, CancellationToken cancellationToken = default) =>
        ExecuteAsync(async service =>
        {
            var result = await service.ArchiveAsync(id, cancellationToken);
            if (!result.IsSuccess) { StatusMessage = result.Error.Message ?? AppStrings.CategoryArchiveFailed; return false; }
            await ReloadAsync(cancellationToken);
            return true;
        }, AppStrings.CategoryArchiveFailed, cancellationToken);

    private async Task<bool> ExecuteAsync(
        Func<AccountService, Task<bool>> action,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        if (IsBusy) return false;
        IsBusy = true;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            return await action(scope.ServiceProvider.GetRequiredService<AccountService>());
        }
        catch (Exception)
        {
            StatusMessage = failureMessage;
            return false;
        }
        finally { IsBusy = false; }
    }

    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AccountService>();
        var result = await service.GetAccountsAsync(cancellationToken: cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            StatusMessage = AppStrings.CategoriesLoadFailed;
            return;
        }

        Categories.Clear();
        foreach (var item in result.Value
                     .Where(x => x.Account.Kind == AccountKind.Category)
                     .OrderBy(x => x.Account.Type)
                     .ThenBy(x => x.Account.SortOrder)
                     .ThenBy(x => x.Account.Name))
        {
            Categories.Add(new CategoryListItem(item.Account.Id, item.Account.Name,
                item.Account.Type == AccountType.Expense ? AppStrings.ExpenseCategory : AppStrings.IncomeCategory, item.Account.CurrencyCode));
        }
        StatusMessage = Categories.Count == 0 ? AppStrings.CategoriesEmpty : string.Empty;
    }
}

public sealed record CategoryListItem(Guid Id, string Name, string TypeName, string CurrencyCode);
