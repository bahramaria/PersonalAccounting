using Hesabyar.Domain.Ledger;
using Hesabyar.Domain.ValueObjects;
namespace Hesabyar.Domain.Tests;
public sealed class LedgerTransactionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 8, 0, 0, TimeSpan.Zero);
    [Fact]
    public void Create_WhenPostingsBalance_PerCurrency_CreatesTransaction()
    {
        var transaction = LedgerTransaction.Create(new(2026, 11, 13), TransactionKind.Expense,
            [new Posting(Guid.CreateVersion7(), new(125_000, CurrencyCode.Irr)), new Posting(Guid.CreateVersion7(), new(-125_000, CurrencyCode.Irr))], Now);
        Assert.Equal(2, transaction.Postings.Count);
    }
    [Fact]
    public void Create_WhenPostingsDoNotBalance_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => LedgerTransaction.Create(new(2026, 11, 13), TransactionKind.Expense,
            [new Posting(Guid.CreateVersion7(), new(100, CurrencyCode.Irr)), new Posting(Guid.CreateVersion7(), new(-90, CurrencyCode.Irr))], Now));
        Assert.Contains("not balanced", exception.Message);
    }
    [Fact]
    public void Create_WhenCurrencyGroupsAreNotIndividuallyBalanced_Throws()
    {
        var usd = new CurrencyCode("USD");
        Assert.Throws<InvalidOperationException>(() => LedgerTransaction.Create(new(2026, 11, 13), TransactionKind.Transfer,
            [new Posting(Guid.CreateVersion7(), new(100, CurrencyCode.Irr)), new Posting(Guid.CreateVersion7(), new(-100, usd))], Now));
    }
}