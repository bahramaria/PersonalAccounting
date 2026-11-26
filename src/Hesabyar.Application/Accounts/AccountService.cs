using Hesabyar.Application.Common;
using Hesabyar.Domain.Accounts;
using Hesabyar.Domain.Ledger;
using Hesabyar.Domain.ValueObjects;

namespace Hesabyar.Application.Accounts;

public sealed class AccountService(IAccountingRepository repository, IClock clock)
{
    public async Task<Result<Guid>> CreateAsync(CreateAccountRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Result<Guid>.Failure("account.name.required", "Account name is required.");

        if (request.Name.Trim().Length > 120)
            return Result<Guid>.Failure("account.name.too_long", "Account name cannot exceed 120 characters.");

        if (request.OpeningBalanceMinor < 0)
            return Result<Guid>.Failure("account.opening_balance.invalid", "Opening balance cannot be negative.");

        if (!TryValidateKind(request.Type, request.Kind, out var validationError))
            return Result<Guid>.Failure("account.kind.invalid", validationError);

        CurrencyCode currency;
        try
        {
            currency = new CurrencyCode(request.CurrencyCode);
        }
        catch (ArgumentException)
        {
            return Result<Guid>.Failure("account.currency.invalid", "Currency code is invalid.");
        }

        if (request.Kind == AccountKind.Category && request.ParentId is Guid parentId)
        {
            var parent = await repository.GetAccountAsync(parentId, cancellationToken);
            if (parent is null || parent.IsArchived || parent.Kind != AccountKind.Category ||
                parent.Type != request.Type || parent.ParentId is not null)
            {
                return Result<Guid>.Failure("account.parent.invalid", "A category can only have a root category of the same type as its parent.");
            }
        }
        else if (request.Kind != AccountKind.Category && request.ParentId is not null)
        {
            return Result<Guid>.Failure("account.parent.not_supported", "Only categories can have a parent.");
        }

        if (request.CardLast4 is not null &&
            (request.CardLast4.Length != 4 || request.CardLast4.Any(c => !char.IsAsciiDigit(c))))
        {
            return Result<Guid>.Failure("account.card_last4.invalid", "Card suffix must contain exactly four digits.");
        }

        if (request.BankName?.Length > 80 || request.AccountNumberMasked?.Length > 40)
            return Result<Guid>.Failure("account.bank_details.too_long", "Bank account details exceed the supported length.");

        LedgerTransaction? openingTransaction = null;
        var nowUtc = clock.UtcNow;

        var account = new Account(
            request.Name,
            request.Type,
            request.Kind,
            currency,
            nowUtc,
            request.ParentId,
            request.Icon,
            request.Color,
            request.SortOrder,
            request.BankName,
            request.CardLast4,
            request.AccountNumberMasked);

        if (request.OpeningBalanceMinor > 0)
        {
            if (request.Type is not (AccountType.Asset or AccountType.Liability))
                return Result<Guid>.Failure("account.opening_balance.not_allowed", "Opening balance is only supported for asset and liability accounts.");

            if (request.OpeningDate is null)
                return Result<Guid>.Failure("account.opening_date.required", "Opening date is required when an opening balance is provided.");

            var openingAccountId = await repository.GetOrCreateOpeningAccountAsync(currency, nowUtc, cancellationToken);
            var amount = new Money(request.OpeningBalanceMinor, currency);
            var accountAmount = request.Type == AccountType.Asset ? amount : amount.Negate();

            openingTransaction = LedgerTransaction.Create(
                request.OpeningDate.Value,
                TransactionKind.Opening,
                [
                    new Posting(account.Id, accountAmount),
                    new Posting(openingAccountId, accountAmount.Negate())
                ],
                nowUtc,
                "Opening balance");
        }

        var accountId = await repository.CreateAccountAsync(account, openingTransaction, cancellationToken);
        return Result<Guid>.Success(accountId);
    }

    public async Task<Result<IReadOnlyList<AccountBalance>>> GetAccountsAsync(
        bool includeArchived = false,
        CancellationToken cancellationToken = default)
    {
        var accounts = await repository.GetAccountsAsync(includeArchived, cancellationToken);
        var accountIds = accounts.Select(x => x.Id).ToArray();
        var balances = await repository.GetBalancesMinorAsync(accountIds, cancellationToken);
        var result = accounts
            .Select(account => new AccountBalance(
                account,
                balances.TryGetValue(account.Id, out var balance) ? balance : 0L))
            .ToArray();

        return Result<IReadOnlyList<AccountBalance>>.Success(result);
    }

    public async Task<Result<bool>> RenameAsync(Guid accountId, string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120)
            return Result<bool>.Failure("account.name.invalid", "Account name must contain between 1 and 120 characters.");

        var account = await repository.GetAccountAsync(accountId, cancellationToken);
        if (account is null)
            return Result<bool>.Failure("account.not_found", "Account was not found.");

        if (account.Kind == AccountKind.System)
            return Result<bool>.Failure("account.system.read_only", "System accounts cannot be renamed.");

        if (account.IsArchived)
            return Result<bool>.Failure("account.archived", "Archived accounts cannot be renamed.");

        await repository.RenameAccountAsync(accountId, name.Trim(), clock.UtcNow, cancellationToken);
        return Result<bool>.Success(true);
    }

    public async Task<Result<bool>> ArchiveAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var account = await repository.GetAccountAsync(accountId, cancellationToken);
        if (account is null)
            return Result<bool>.Failure("account.not_found", "Account was not found.");

        if (account.Kind == AccountKind.System)
            return Result<bool>.Failure("account.system.read_only", "System accounts cannot be archived.");

        if (account.IsArchived)
            return Result<bool>.Success(true);

        if (account.Kind == AccountKind.Category &&
            await repository.HasActiveChildCategoriesAsync(accountId, cancellationToken))
        {
            return Result<bool>.Failure(
                "account.category.has_active_children",
                "Active child categories must be archived before archiving their parent.");
        }

        await repository.ArchiveAccountAsync(accountId, clock.UtcNow, cancellationToken);
        return Result<bool>.Success(true);
    }

    public async Task<Result<bool>> RestoreAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var account = await repository.GetAccountAsync(accountId, cancellationToken);
        if (account is null)
            return Result<bool>.Failure("account.not_found", "Account was not found.");

        if (account.Kind == AccountKind.System)
            return Result<bool>.Failure("account.system.read_only", "System accounts cannot be restored.");

        if (!account.IsArchived)
            return Result<bool>.Success(true);

        await repository.RestoreAccountAsync(accountId, clock.UtcNow, cancellationToken);
        return Result<bool>.Success(true);
    }

    private static bool TryValidateKind(AccountType type, AccountKind kind, out string error)
    {
        var valid = kind switch
        {
            AccountKind.Category => type is AccountType.Income or AccountType.Expense,
            AccountKind.CreditCard => type == AccountType.Liability,
            AccountKind.Cash or AccountKind.Bank or AccountKind.Card or AccountKind.Wallet => type == AccountType.Asset,
            AccountKind.System => false,
            _ => false
        };

        error = valid ? string.Empty : "Account type and kind are incompatible, or the selected kind is reserved for internal use.";
        return valid;
    }
}
