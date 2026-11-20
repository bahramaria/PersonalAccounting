using Hesabyar.Application.Sms;

namespace Hesabyar.Infrastructure.Tests;

public sealed class BankSmsParserCurrencyAmbiguityTests
{
    [Fact]
    public void ConflictingTomanAndRialUnitsRequireManualReviewEvenWhenBankRuleMatches()
    {
        var rule = new BankSmsRule(
            "test-bank",
            ["TestBank"],
            ["برداشت"],
            ["واریز"],
            AmountMultiplier: 1);
        var parser = new BankSmsParser([rule]);

        var result = parser.Parse(
            "TestBank",
            "برداشت مبلغ: ۲۵۰ تومان، مانده: ۵۰۰ ریال");

        Assert.Equal(BankSmsParseStatus.NeedsReview, result.Status);
        Assert.Equal("test-bank", result.BankKey);
        Assert.Equal(250, result.AmountMinor);
        Assert.Equal(500, result.BalanceMinor);
        Assert.False(result.AmountUnitKnown);
    }
}
