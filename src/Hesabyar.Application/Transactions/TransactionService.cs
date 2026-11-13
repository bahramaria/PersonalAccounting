using Hesabyar.Application.Accounts;
using Hesabyar.Application.Common;
using Hesabyar.Domain.Accounts;
using Hesabyar.Domain.Ledger;
using Hesabyar.Domain.ValueObjects;

namespace Hesabyar.Application.Transactions;

public sealed class TransactionService(IAccountingRepository repository, IClock clock)
{
    public async Task<Result<Guid>> CreateExpenseAsync(ExpenseRequest request, CancellationToken cancellationToken = default)
    {
        if (request.AmountMinor <= 0)
            return Result<Guid>.Failure("transaction.amount.invalid", "Amount must be greater than zero.");

        var paymentAccount = await repository.GetAccountAsync(request.PaymentAccountId, cancellationToken);
        if (!IsActive(paymentAccount))
            return Result<Guid>.Failure("transaction.account.invalid", "Payment account was not found or is archived.");

        if (paymentAccount!.Type is not (AccountType.Asset or AccountType.Liability))
            return Result<Guid>.Failure("transaction.payment_account.invalid", "Payment account must be an asset or liability account.");

        var category = await repository.GetAccountAsync(request.ExpenseCategoryId, cancellationToken);
        if (!IsActive(category) || category!.Kind != AccountKind.Category || category.Type != AccountType.Expense)
            return Result<Guid>.Failure("transaction.category.invalid", "An active expense category is required.");

        if (!string.Equals(paymentAccount.CurrencyCode, category.CurrencyCode, StringComparison.Ordinal))
            return Result<Guid>.Failure("transaction.currency.mismatch", "Payment account and category must use the same currency.");

        var currency = new CurrencyCode(paymentAccount.CurrencyCode);
        var amount = new Money(request.AmountMinor, currency);
        var transaction = LedgerTransaction.Create(
            request.BusinessDate,
            TransactionKind.Expense,
            [
                new Posting(category.Id, amount),
                new Posting(paymentAccount.Id, amount.Negate())
            ],
            clock.UtcNow,
            request.Description);

        return Result<Guid>.Success(await repository.CreateTransactionAsync(transaction, cancellationToken));
    }

    public async Task<Result<Guid>> CreateIncomeAsync(IncomeRequest request, CancellationToken cancellationToken = default)
    {
        if (request.AmountMinor <= 0)
            return Result<Guid>.Failure("transaction.amount.invalid", "Amount must be greater than zero.");

        var depositAccount = await repository.GetAccountAsync(request.DepositAccountId, cancellationToken);
        if (!IsActive(depositAccount) || depositAccount!.Type != AccountType.Asset)
            return Result<Guid>.Failure("transaction.deposit_account.invalid", "An active asset account is required.");

        var category = await repository.GetAccountAsync(request.IncomeCategoryId, cancellationToken);
        if (!IsActive(category) || category!.Kind != AccountKind.Category || category.Type != AccountType.Income)
            return Result<Guid>.Failure("transaction.category.invalid", "An active income category is required.");

        if (!string.Equals(depositAccount.CurrencyCode, category.CurrencyCode, StringComparison.Ordinal))
            return Result<Guid>.Failure("transaction.currency.mismatch", "Deposit account and category must use the same currency.");

        var currency = new CurrencyCode(depositAccount.CurrencyCode);
        var amount = new Money(request.AmountMinor, currency);
        var transaction = LedgerTransaction.Create(
            request.BusinessDate,
            TransactionKind.Income,
            [
                new Posting(depositAccount.Id, amount),
                new Posting(category.Id, amount.Negate())
            ],
            clock.UtcNow,
            request.Description);

        return Result<Guid>.Success(await repository.CreateTransactionAsync(transaction, cancellationToken));
    }

    public async Task<Result<Guid>> CreateTransferAsync(TransferRequest request, CancellationToken cancellationToken = default)
    {
        if (request.AmountMinor <= 0)
            return Result<Guid>.Failure("transaction.amount.invalid", "Amount must be greater than zero.");

        if (request.SourceAccountId == request.DestinationAccountId)
            return Result<Guid>.Failure("transaction.transfer.same_account", "Source and destination accounts must be different.");

        var source = await repository.GetAccountAsync(request.SourceAccountId, cancellationToken);
        var destination = await repository.GetAccountAsync(request.DestinationAccountId, cancellationToken);

        if (!IsActive(source) || !IsActive(destination))
            return Result<Guid>.Failure("transaction.account.invalid", "Both transfer accounts must exist and be active.");

        if (source!.Type != AccountType.Asset || destination!.Type != AccountType.Asset)
            return Result<Guid>.Failure("transaction.transfer.account_type", "Transfers are currently supported between asset accounts.");

        if (!string.Equals(source.CurrencyCode, destination.CurrencyCode, StringComparison.Ordinal))
            return Result<Guid>.Failure("transaction.currency.mismatch", "Transfer accounts must use the same currency.");

        var amount = new Money(request.AmountMinor, new CurrencyCode(source.CurrencyCode));
        var transaction = LedgerTransaction.Create(
            request.BusinessDate,
            TransactionKind.Transfer,
            [
                new Posting(destination.Id, amount),
                new Posting(source.Id, amount.Negate())
            ],
            clock.UtcNow,
            request.Description);

        return Result<Guid>.Success(await repository.CreateTransactionAsync(transaction, cancellationToken));
    }

    public async Task<Result<Guid>> UpdateAsync(
        Guid transactionId,
        ExpenseRequest? expense = null,
        IncomeRequest? income = null,
        TransferRequest? transfer = null,
        CancellationToken cancellationToken = default)
    {
        var transactions = await repository.GetTransactionsAsync(null, null, null, cancellationToken);
        var existing = transactions.SingleOrDefault(x => x.Id == transactionId);
        if (existing is null)
            return Result<Guid>.Failure("transaction.not_found", "Transaction was not found.");

        var replacement = existing.Kind switch
        {
            TransactionKind.Expense when expense is not null => await CreateExpenseAsync(expense, cancellationToken),
            TransactionKind.Income when income is not null => await CreateIncomeAsync(income, cancellationToken),
            TransactionKind.Transfer when transfer is not null => await CreateTransferAsync(transfer, cancellationToken),
            _ => Result<Guid>.Failure("transaction.update.invalid", "The replacement does not match the transaction type.")
        };

        if (replacement.IsFailure)
            return replacement;

        await repository.SoftDeleteTransactionAsync(transactionId, clock.UtcNow, cancellationToken);
        return replacement;
    }

    public Task<IReadOnlyList<TransactionSnapshot>> GetTransactionsAsync(
        string? search = null,
        DateOnly? fromDate = null,
        DateOnly? toDate = null,
        CancellationToken cancellationToken = default)
    {
        if (fromDate is not null && toDate is not null && fromDate > toDate)
            throw new ArgumentException("The start date cannot be later than the end date.");

        return repository.GetTransactionsAsync(search, fromDate, toDate, cancellationToken);
    }

    public async Task<Result<bool>> DeleteAsync(Guid transactionId, CancellationToken cancellationToken = default)
    {
        var transactions = await repository.GetTransactionsAsync(null, null, null, cancellationToken);
        if (!transactions.Any(x => x.Id == transactionId))
            return Result<bool>.Failure("transaction.not_found", "Transaction was not found.");

        await repository.SoftDeleteTransactionAsync(transactionId, clock.UtcNow, cancellationToken);
        return Result<bool>.Success(true);
    }

    public async Task<Result<Guid>> DuplicateAsync(Guid transactionId, CancellationToken cancellationToken = default)
    {
        var transactions = await repository.GetTransactionsAsync(null, null, null, cancellationToken);
        var item = transactions.SingleOrDefault(x => x.Id == transactionId);
        if (item is null)
            return Result<Guid>.Failure("transaction.not_found", "Transaction was not found.");

        var date = clock.UtcNow.Date;
        var businessDate = DateOnly.FromDateTime(date);
        Result<Guid> created = item.Kind switch
        {
            TransactionKind.Expense when item.CounterpartyAccountId is Guid categoryId =>
                await CreateExpenseAsync(new ExpenseRequest(businessDate, item.AmountMinor, item.AccountId, categoryId, item.Description), cancellationToken),
            TransactionKind.Income when item.CounterpartyAccountId is Guid categoryId =>
                await CreateIncomeAsync(new IncomeRequest(businessDate, item.AmountMinor, item.AccountId, categoryId, item.Description), cancellationToken),
            TransactionKind.Transfer when item.CounterpartyAccountId is Guid destinationId =>
                await CreateTransferAsync(new TransferRequest(businessDate, item.AmountMinor, item.AccountId, destinationId, item.Description), cancellationToken),
            _ => Result<Guid>.Failure("transaction.duplicate.unsupported", "This transaction cannot be duplicated.")
        };

        return created;
    }

    private static bool IsActive(AccountSnapshot? account) => account is not null && !account.IsArchived;
}
