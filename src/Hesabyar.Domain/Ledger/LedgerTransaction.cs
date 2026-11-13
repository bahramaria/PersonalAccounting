using Hesabyar.Domain.Common;
using Hesabyar.Domain.ValueObjects;

namespace Hesabyar.Domain.Ledger;

public enum TransactionKind { Expense, Income, Transfer, Opening, Adjustment, PersonDebt, PersonRepay, LoanPayment, ChequeEvent }
public enum TransactionSource { Manual, Sms, Import }

public sealed class LedgerTransaction : EntityBase
{
    private readonly List<Posting> _postings = [];

    private LedgerTransaction(
        DateOnly businessDate,
        TransactionKind kind,
        string? description,
        TransactionSource source,
        DateTimeOffset nowUtc) : base(nowUtc)
    {
        BusinessDate = businessDate;
        Kind = kind;
        Description = description?.Trim();
        Source = source;
        IsConfirmed = source == TransactionSource.Manual;
    }

    public DateOnly BusinessDate { get; }
    public TransactionKind Kind { get; }
    public string? Description { get; }
    public TransactionSource Source { get; }
    public bool IsConfirmed { get; private set; }
    public IReadOnlyCollection<Posting> Postings => _postings.AsReadOnly();

    public static LedgerTransaction Create(
        DateOnly businessDate,
        TransactionKind kind,
        IEnumerable<Posting> postings,
        DateTimeOffset nowUtc,
        string? description = null,
        TransactionSource source = TransactionSource.Manual)
    {
        ArgumentNullException.ThrowIfNull(postings);
        var items = postings.ToArray();
        if (items.Length < 2)
            throw new ArgumentException("A ledger transaction requires at least two postings.", nameof(postings));

        foreach (var group in items.GroupBy(x => x.Amount.Currency))
        {
            long total = 0;
            foreach (var posting in group)
                total = checked(total + posting.Amount.Minor);

            if (total != 0)
                throw new InvalidOperationException($"Ledger transaction is not balanced for currency {group.Key}.");
        }

        if (items.Any(x => x.AccountId == Guid.Empty))
            throw new ArgumentException("Posting account identifiers cannot be empty.", nameof(postings));

        var transaction = new LedgerTransaction(businessDate, kind, description, source, nowUtc);
        transaction._postings.AddRange(items);
        return transaction;
    }

    public void Confirm(DateTimeOffset nowUtc)
    {
        IsConfirmed = true;
        MarkUpdated(nowUtc);
    }
}
