using Hesabyar.Application.Accounts;
using Hesabyar.Application.Common;
using Hesabyar.Domain.Accounts;
using Hesabyar.Domain.Ledger;
using Hesabyar.Domain.ValueObjects;

namespace Hesabyar.Application.Transactions;

public sealed class TransactionService(IAccountingRepository repository, IClock clock)
{
    public Task<Result<Guid>> CreateExpenseAsync(ExpenseRequest request, CancellationToken cancellationToken = default) =>
        CreateAsync(BuildExpenseAsync(request, cancellationToken), cancellationToken);

    public Task<Result<Guid>> CreateIncomeAsync(IncomeRequest request, CancellationToken cancellationToken = default) =>
        CreateAsync(BuildIncomeAsync(request, cancellationToken), cancellationToken);

    public Task<Result<Guid>> CreateTransferAsync(TransferRequest request, CancellationToken cancellationToken = default) =>
        CreateAsync(BuildTransferAsync(request, cancellationToken), cancellationToken);

    public Task<Result<Guid>> CreateCurrencyExchangeAsync(CurrencyExchangeRequest request, CancellationToken cancellationToken = default) =>
        CreateAsync(BuildCurrencyExchangeAsync(request, cancellationToken), cancellationToken);

    public async Task<Result<Guid>> UpdateAsync(
        Guid transactionId,
        ExpenseRequest? expense = null,
        IncomeRequest? income = null,
        TransferRequest? transfer = null,
        CancellationToken cancellationToken = default,
        CurrencyExchangeRequest? currencyExchange = null)
    {
        var existing = await repository.GetTransactionAsync(transactionId, cancellationToken);
        if (existing is null)
            return Result<Guid>.Failure("transaction.not_found", "Transaction was not found.");

        var replacement = existing.Kind switch
        {
            TransactionKind.Expense when expense is not null => await BuildExpenseAsync(expense, cancellationToken),
            TransactionKind.Income when income is not null => await BuildIncomeAsync(income, cancellationToken),
            TransactionKind.Transfer when transfer is not null => await BuildTransferAsync(transfer, cancellationToken),
            TransactionKind.CurrencyExchange when currencyExchange is not null => await BuildCurrencyExchangeAsync(currencyExchange, cancellationToken),
            _ => Result<LedgerTransaction>.Failure("transaction.update.invalid", "The replacement does not match the transaction type.")
        };

        if (replacement.IsFailure)
            return Result<Guid>.Failure(replacement.Error.Code, replacement.Error.Message ?? "The replacement transaction is invalid.");

        var replaced = await repository.ReplaceTransactionAsync(transactionId, replacement.Value!, clock.UtcNow, cancellationToken);
        return replaced
            ? Result<Guid>.Success(replacement.Value!.Id)
            : Result<Guid>.Failure("transaction.not_found", "Transaction was not found.");
    }

    public Task<IReadOnlyList<TransactionSnapshot>> GetTransactionsAsync(
        string? search = null,
        DateOnly? fromDate = null,
        DateOnly? toDate = null,
        Guid? accountId = null,
        Guid? categoryId = null,
        long? minimumAmountMinor = null,
        long? maximumAmountMinor = null,
        TransactionSortOrder sortOrder = TransactionSortOrder.NewestFirst,
        int take = 50_000,
        CancellationToken cancellationToken = default,
        int skip = 0)
    {
        if (skip < 0)
            throw new ArgumentOutOfRangeException(nameof(skip));
        if (fromDate is not null && toDate is not null && fromDate > toDate)
            throw new ArgumentException("The start date cannot be later than the end date.");
        if (take is < 1 or > 50_000)
            throw new ArgumentOutOfRangeException(nameof(take), "The transaction result limit must be between 1 and 50,000.");
        if (minimumAmountMinor is < 0 || maximumAmountMinor is < 0 ||
            minimumAmountMinor is not null && maximumAmountMinor is not null && minimumAmountMinor > maximumAmountMinor)
            throw new ArgumentException("The transaction amount range is invalid.");

        return repository.GetTransactionsAsync(search, fromDate, toDate, accountId, categoryId,
            minimumAmountMinor, maximumAmountMinor, sortOrder, skip, take, cancellationToken);
    }

    public async Task<Result<bool>> DeleteAsync(Guid transactionId, CancellationToken cancellationToken = default)
    {
        var existing = await repository.GetTransactionAsync(transactionId, cancellationToken);
        if (existing is null)
            return Result<bool>.Failure("transaction.not_found", "Transaction was not found.");

        await repository.SoftDeleteTransactionAsync(transactionId, clock.UtcNow, cancellationToken);
        return Result<bool>.Success(true);
    }

    public async Task<Result<Guid>> DuplicateAsync(Guid transactionId, CancellationToken cancellationToken = default)
    {
        var item = await repository.GetTransactionAsync(transactionId, cancellationToken);
        if (item is null)
            return Result<Guid>.Failure("transaction.not_found", "Transaction was not found.");

        var businessDate = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        return item.Kind switch
        {
            TransactionKind.Expense when item.CounterpartyAccountId is Guid categoryId =>
                await CreateExpenseAsync(new ExpenseRequest(businessDate, item.AmountMinor, item.AccountId, categoryId, item.Description), cancellationToken),
            TransactionKind.Income when item.CounterpartyAccountId is Guid categoryId =>
                await CreateIncomeAsync(new IncomeRequest(businessDate, item.AmountMinor, item.AccountId, categoryId, item.Description), cancellationToken),
            TransactionKind.Transfer when item.CounterpartyAccountId is Guid destinationId =>
                await CreateTransferAsync(new TransferRequest(businessDate, item.AmountMinor, item.AccountId, destinationId, item.Description), cancellationToken),
            TransactionKind.CurrencyExchange when item.CounterpartyAccountId is Guid destinationId && item.CounterAmountMinor is long destinationAmount =>
                await CreateCurrencyExchangeAsync(new CurrencyExchangeRequest(businessDate, item.AmountMinor, destinationAmount,
                    item.AccountId, destinationId, item.Description), cancellationToken),
            _ => Result<Guid>.Failure("transaction.duplicate.unsupported", "This transaction cannot be duplicated.")
        };
    }

    private async Task<Result<Guid>> CreateAsync(Task<Result<LedgerTransaction>> buildTask, CancellationToken cancellationToken)
    {
        var built = await buildTask;
        if (built.IsFailure)
            return Result<Guid>.Failure(built.Error.Code, built.Error.Message ?? "The transaction is invalid.");

        return Result<Guid>.Success(await repository.CreateTransactionAsync(built.Value!, cancellationToken));
    }

    private async Task<Result<LedgerTransaction>> BuildExpenseAsync(ExpenseRequest request, CancellationToken cancellationToken)
    {
        if (request.AmountMinor <= 0)
            return Result<LedgerTransaction>.Failure("transaction.amount.invalid", "Amount must be greater than zero.");

        var paymentAccount = await repository.GetAccountAsync(request.PaymentAccountId, cancellationToken);
        if (!IsActive(paymentAccount))
            return Result<LedgerTransaction>.Failure("transaction.account.invalid", "Payment account was not found or is archived.");
        if (paymentAccount!.Type is not (AccountType.Asset or AccountType.Liability))
            return Result<LedgerTransaction>.Failure("transaction.payment_account.invalid", "Payment account must be an asset or liability account.");

        var category = await repository.GetAccountAsync(request.ExpenseCategoryId, cancellationToken);
        if (!IsActive(category) || category!.Kind != AccountKind.Category || category.Type != AccountType.Expense)
            return Result<LedgerTransaction>.Failure("transaction.category.invalid", "An active expense category is required.");
        if (!string.Equals(paymentAccount.CurrencyCode, category.CurrencyCode, StringComparison.Ordinal))
            return Result<LedgerTransaction>.Failure("transaction.currency.mismatch", "Payment account and category must use the same currency.");

        var amount = new Money(request.AmountMinor, new CurrencyCode(paymentAccount.CurrencyCode));
        return Result<LedgerTransaction>.Success(LedgerTransaction.Create(request.BusinessDate, TransactionKind.Expense,
            [new Posting(category.Id, amount), new Posting(paymentAccount.Id, amount.Negate())], clock.UtcNow, request.Description));
    }

    private async Task<Result<LedgerTransaction>> BuildIncomeAsync(IncomeRequest request, CancellationToken cancellationToken)
    {
        if (request.AmountMinor <= 0)
            return Result<LedgerTransaction>.Failure("transaction.amount.invalid", "Amount must be greater than zero.");

        var depositAccount = await repository.GetAccountAsync(request.DepositAccountId, cancellationToken);
        if (!IsActive(depositAccount) || depositAccount!.Type != AccountType.Asset)
            return Result<LedgerTransaction>.Failure("transaction.deposit_account.invalid", "An active asset account is required.");
        var category = await repository.GetAccountAsync(request.IncomeCategoryId, cancellationToken);
        if (!IsActive(category) || category!.Kind != AccountKind.Category || category.Type != AccountType.Income)
            return Result<LedgerTransaction>.Failure("transaction.category.invalid", "An active income category is required.");
        if (!string.Equals(depositAccount.CurrencyCode, category.CurrencyCode, StringComparison.Ordinal))
            return Result<LedgerTransaction>.Failure("transaction.currency.mismatch", "Deposit account and category must use the same currency.");

        var amount = new Money(request.AmountMinor, new CurrencyCode(depositAccount.CurrencyCode));
        return Result<LedgerTransaction>.Success(LedgerTransaction.Create(request.BusinessDate, TransactionKind.Income,
            [new Posting(depositAccount.Id, amount), new Posting(category.Id, amount.Negate())], clock.UtcNow, request.Description));
    }

    private async Task<Result<LedgerTransaction>> BuildTransferAsync(TransferRequest request, CancellationToken cancellationToken)
    {
        if (request.AmountMinor <= 0)
            return Result<LedgerTransaction>.Failure("transaction.amount.invalid", "Amount must be greater than zero.");
        if (request.SourceAccountId == request.DestinationAccountId)
            return Result<LedgerTransaction>.Failure("transaction.transfer.same_account", "Source and destination accounts must be different.");

        var source = await repository.GetAccountAsync(request.SourceAccountId, cancellationToken);
        var destination = await repository.GetAccountAsync(request.DestinationAccountId, cancellationToken);
        if (!IsActive(source) || !IsActive(destination))
            return Result<LedgerTransaction>.Failure("transaction.account.invalid", "Both transfer accounts must exist and be active.");
        if (source!.Type != AccountType.Asset || destination!.Type != AccountType.Asset)
            return Result<LedgerTransaction>.Failure("transaction.transfer.account_type", "Transfers are currently supported between asset accounts.");
        if (!string.Equals(source.CurrencyCode, destination.CurrencyCode, StringComparison.Ordinal))
            return Result<LedgerTransaction>.Failure("transaction.currency.mismatch", "Transfer accounts must use the same currency.");

        var amount = new Money(request.AmountMinor, new CurrencyCode(source.CurrencyCode));
        return Result<LedgerTransaction>.Success(LedgerTransaction.Create(request.BusinessDate, TransactionKind.Transfer,
            [new Posting(destination.Id, amount), new Posting(source.Id, amount.Negate())], clock.UtcNow, request.Description));
    }

    private async Task<Result<LedgerTransaction>> BuildCurrencyExchangeAsync(
        CurrencyExchangeRequest request, CancellationToken cancellationToken)
    {
        if (request.SourceAmountMinor <= 0 || request.DestinationAmountMinor <= 0)
            return Result<LedgerTransaction>.Failure("transaction.exchange.amount.invalid", "Both exchange amounts must be greater than zero.");
        if (request.SourceAccountId == request.DestinationAccountId)
            return Result<LedgerTransaction>.Failure("transaction.transfer.same_account", "Source and destination accounts must be different.");

        var source = await repository.GetAccountAsync(request.SourceAccountId, cancellationToken);
        var destination = await repository.GetAccountAsync(request.DestinationAccountId, cancellationToken);
        if (!IsActive(source) || !IsActive(destination))
            return Result<LedgerTransaction>.Failure("transaction.account.invalid", "Both exchange accounts must exist and be active.");
        if (source!.Type != AccountType.Asset || destination!.Type != AccountType.Asset)
            return Result<LedgerTransaction>.Failure("transaction.transfer.account_type", "Currency exchange requires two asset accounts.");
        if (string.Equals(source.CurrencyCode, destination.CurrencyCode, StringComparison.Ordinal))
            return Result<LedgerTransaction>.Failure("transaction.exchange.same_currency", "Currency exchange requires different currencies.");

        var sourceCurrency = new CurrencyCode(source.CurrencyCode);
        var destinationCurrency = new CurrencyCode(destination.CurrencyCode);
        var sourceClearingId = await repository.GetOrCreateCurrencyExchangeAccountAsync(sourceCurrency, clock.UtcNow, cancellationToken);
        var destinationClearingId = await repository.GetOrCreateCurrencyExchangeAccountAsync(destinationCurrency, clock.UtcNow, cancellationToken);
        var sourceAmount = new Money(request.SourceAmountMinor, sourceCurrency);
        var destinationAmount = new Money(request.DestinationAmountMinor, destinationCurrency);

        return Result<LedgerTransaction>.Success(LedgerTransaction.Create(
            request.BusinessDate,
            TransactionKind.CurrencyExchange,
            [
                new Posting(source.Id, sourceAmount.Negate()),
                new Posting(sourceClearingId, sourceAmount),
                new Posting(destination.Id, destinationAmount),
                new Posting(destinationClearingId, destinationAmount.Negate())
            ],
            clock.UtcNow,
            request.Description,
            counterAmountMinor: request.DestinationAmountMinor));
    }

    private static bool IsActive(AccountSnapshot? account) => account is not null && !account.IsArchived;
}
