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
    public void ConvertsTomanAmountsAndBalancesToRials()
    {
        var parser = new BankSmsParser();
        var result = parser.Parse("Unknown", "برداشت مبلغ: ۲۵۰ تومان مانده: ۵۰۰ تومان");

        Assert.Equal(2_500, result.AmountMinor);
        Assert.Equal(5_000, result.BalanceMinor);
        Assert.True(result.AmountUnitKnown);
    }

    [Fact]
    public void LeavesAmountUnitUnknownWhenMessageDoesNotDeclareIt()
    {
        var parser = new BankSmsParser();
        var result = parser.Parse("Unknown", "برداشت مبلغ: ۲۵۰");

        Assert.Equal(250, result.AmountMinor);
        Assert.False(result.AmountUnitKnown);
    }

    [Fact]
    public void UsesBankRuleMultiplierWhenMessageDoesNotDeclareCurrencyUnit()
    {
        var rule = new BankSmsRule("test", ["TestBank"], ["برداشت"], ["واریز"], AmountMultiplier: 10);
        var parser = new BankSmsParser([rule]);
        var result = parser.Parse("TestBank", "برداشت مبلغ: ۲۵۰");

        Assert.Equal(2_500, result.AmountMinor);
        Assert.True(result.AmountUnitKnown);
    }

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
        Assert.Equal("1405/08/25", result.DateText);
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

        Assert.Equal(BankSmsParseStatus.Unsupported, result.Status);
        Assert.Null(result.AmountMinor);
    }



    [Theory]
    [InlineData("مبلغ:400,000- مانده:6,640,235 08/23 18:18", BankSmsTransactionType.Expense, 400_000L, 6_640_235L, "08/23", "18:18")]
    [InlineData("مبلغ:3,400,000- مانده:7,040,235 08/23 18:01", BankSmsTransactionType.Expense, 3_400_000L, 7_040_235L, "08/23", "18:01")]
    [InlineData("مبلغ:2,130,288- مانده:10,440,235 08/22 10:40", BankSmsTransactionType.Expense, 2_130_288L, 10_440_235L, "08/22", "10:40")]
    [InlineData("مبلغ:10,000,000+ مانده:12,570,523 08/21 19:00", BankSmsTransactionType.Income, 10_000_000L, 12_570_523L, "08/21", "19:00")]
    [InlineData("مبلغ:1,800,000- مانده:2,570,523 08/15 11:07", BankSmsTransactionType.Expense, 1_800_000L, 2_570_523L, "08/15", "11:07")]
    [InlineData("مبلغ:380,000- مانده:4,370,523 08/15 11:05", BankSmsTransactionType.Expense, 380_000L, 4_370_523L, "08/15", "11:05")]
    [InlineData("مبلغ:13,850,000- مانده:4,750,523 08/14 16:13", BankSmsTransactionType.Expense, 13_850_000L, 4_750_523L, "08/14", "16:13")]
    public void ParsesParsianSignedTransactionSamples(
        string body, BankSmsTransactionType expectedType, long expectedAmount,
        long expectedBalance, string expectedDate, string expectedTime)
    {
        var result = new BankSmsParser().Parse("bank-sender", $"12345678901234 {body}");

        Assert.Equal(BankSmsParseStatus.Parsed, result.Status);
        Assert.Equal("parsian", result.BankKey);
        Assert.Equal(expectedType, result.TransactionType);
        Assert.Equal(expectedAmount, result.AmountMinor);
        Assert.Equal(expectedBalance, result.BalanceMinor);
        Assert.Equal(expectedDate, result.DateText);
        Assert.Equal(TimeOnly.Parse(expectedTime), result.TransactionTime);
        Assert.True(result.AmountUnitKnown);
    }

    [Theory]
    [InlineData("پرداخت قبض تلفن ثابت مبلغ 4,139,000 رمز: 11111111")]
    [InlineData("پرداخت قبض تلفن ثابت مبلغ 4,139,000 رمز: 22222222")]
    public void VerificationCodeMessagesAreUnsupported(string body)
    {
        var result = new BankSmsParser().Parse("bank-sender", body);

        Assert.Equal(BankSmsParseStatus.Unsupported, result.Status);
        Assert.Null(result.AmountMinor);
    }


    [Fact]
    public void PurchaseWithReferenceCodeRemainsInManualReview()
    {
        var result = new BankSmsParser().Parse(
            "bank-sender", "خرید قبضینو مبلغ 2,130,288 Code:00000000");

        Assert.Equal(BankSmsParseStatus.NeedsReview, result.Status);
        Assert.Equal(BankSmsTransactionType.Expense, result.TransactionType);
        Assert.Equal(2_130_288, result.AmountMinor);
        Assert.False(result.AmountUnitKnown);
    }

    [Fact]
    public void MessageThatOnlyMentionsAnAmountIsNotImportedAsFinancial()
    {
        var result = new BankSmsParser().Parse("sender", "مبلغی که درباره‌اش صحبت کردیم فردا آماده می‌شود.");

        Assert.Equal(BankSmsParseStatus.Unsupported, result.Status);
    }

    [Fact]
    public void UnrelatedPersonalMessageIsUnsupported()
    {
        var result = new BankSmsParser().Parse("sender", "سلام، جلسه فردا ساعت ۱۰ برگزار می‌شود.");

        Assert.Equal(BankSmsParseStatus.Unsupported, result.Status);
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
