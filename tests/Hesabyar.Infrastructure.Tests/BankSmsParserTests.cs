using Hesabyar.Application.Sms;

namespace Hesabyar.Infrastructure.Tests;

public sealed class BankSmsParserTests
{
    private static readonly BankSmsRule MellatRule = new(
        "mellat",
        ["Mellat", "ملت"],
        ["برداشت", "خرید"],
        ["واریز", "دریافت"]);

    [Fact]
    public void ParsesPersianDigitsAndExpenseDetails()
    {
        var parser = new BankSmsParser([MellatRule]);

        var result = parser.Parse("Mellat", "برداشت مبلغ: ۱٬۲۵۰٬۰۰۰ ریال کارت ۱۲۳۴ مانده: ۵٬۰۰۰٬۰۰۰ تاریخ ۱۴۰۵/۰۸/۲۵ ساعت 09:35");

        Assert.Equal(BankSmsParseStatus.Parsed, result.Status);
        Assert.Equal(BankSmsTransactionType.Expense, result.TransactionType);
        Assert.Equal(1_250_000, result.AmountMinor);
        Assert.Equal(5_000_000, result.BalanceMinor);
        Assert.Equal("1234", result.CardLastFour);
        Assert.Equal(new DateOnly(1405, 8, 25), result.BusinessDate);
        Assert.Equal(new TimeOnly(9, 35), result.TransactionTime);
    }

    [Fact]
    public void ParsesArabicIndicDigitsAndIncome()
    {
        var parser = new BankSmsParser([MellatRule]);

        var result = parser.Parse("ملت", "واریز مبلغ: ٢٥٠٬٠٠٠ ریال");

        Assert.Equal(BankSmsParseStatus.Parsed, result.Status);
        Assert.Equal(BankSmsTransactionType.Income, result.TransactionType);
        Assert.Equal(250_000, result.AmountMinor);
    }

    [Fact]
    public void UnknownSenderRequiresReviewEvenWhenAmountCanBeRead()
    {
        var parser = new BankSmsParser([MellatRule]);

        var result = parser.Parse("UnknownSender", "برداشت مبلغ: ۵۰۰۰۰۰ ریال");

        Assert.Equal(BankSmsParseStatus.NeedsReview, result.Status);
        Assert.Equal(BankSmsTransactionType.Expense, result.TransactionType);
        Assert.Equal(500_000, result.AmountMinor);
        Assert.Null(result.BankKey);
    }

    [Fact]
    public void UnknownFinancialDirectionRequiresReview()
    {
        var parser = new BankSmsParser([MellatRule]);

        var result = parser.Parse("Mellat", "تراکنش مبلغ: ۵۰۰۰۰۰ ریال");

        Assert.Equal(BankSmsParseStatus.NeedsReview, result.Status);
        Assert.Equal(BankSmsTransactionType.Unknown, result.TransactionType);
    }

    [Fact]
    public void NonFinancialMessageIsNotParsed()
    {
        var parser = new BankSmsParser([MellatRule]);

        var result = parser.Parse("Mellat", "رمز پویا: ۱۲۳۴۵۶");

        Assert.Equal(BankSmsParseStatus.NeedsReview, result.Status);
        Assert.Null(result.AmountMinor);
    }

    [Fact]
    public void EmptyMessageIsUnsupported()
    {
        var parser = new BankSmsParser([MellatRule]);

        var result = parser.Parse("Mellat", " ");

        Assert.Equal(BankSmsParseStatus.Unsupported, result.Status);
    }

    [Fact]
    public void DedupeHashIsStableForSameSenderAndBody()
    {
        var parser = new BankSmsParser([MellatRule]);

        var first = parser.Parse("Mellat", "برداشت مبلغ: ۱۰۰۰");
        var second = parser.Parse("Mellat", "برداشت مبلغ: ۱۰۰۰");

        Assert.Equal(first.DedupeHash, second.DedupeHash);
        Assert.Equal(64, first.DedupeHash.Length);
    }

    [Fact]
    public void DedupeHashChangesWhenMessageChanges()
    {
        var parser = new BankSmsParser([MellatRule]);

        var first = parser.Parse("Mellat", "برداشت مبلغ: ۱۰۰۰");
        var second = parser.Parse("Mellat", "برداشت مبلغ: ۲۰۰۰");

        Assert.NotEqual(first.DedupeHash, second.DedupeHash);
    }
}
