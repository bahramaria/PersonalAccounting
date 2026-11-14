using Hesabyar.Application.Security;

namespace Hesabyar.Infrastructure.Tests;

public sealed class PinHasherTests
{
    [Theory]
    [InlineData("۱۲۳۴", "1234")]
    [InlineData("١٢٣٤", "1234")]
    [InlineData("1234", "1234")]
    public void NormalizePin_ConvertsSupportedDigits(string input, string expected) =>
        Assert.Equal(expected, PinHasher.NormalizePin(input));

    [Theory]
    [InlineData("123")]
    [InlineData("123456789")]
    [InlineData("12a4")]
    [InlineData("")]
    public void IsValidPin_RejectsInvalidValues(string pin) =>
        Assert.False(PinHasher.IsValidPin(pin));

    [Fact]
    public void Hash_VerifiesCorrectPin_AndRejectsIncorrectPin()
    {
        var salt = PinHasher.CreateSalt();
        var hash = PinHasher.Hash("۱۲۳۴", salt);
        Assert.True(PinHasher.Verify("1234", salt, hash, PinHasher.DefaultIterations));
        Assert.False(PinHasher.Verify("4321", salt, hash, PinHasher.DefaultIterations));
    }

    [Fact]
    public void CreateSalt_ReturnsUniqueValues()
    {
        var first = PinHasher.CreateSalt();
        var second = PinHasher.CreateSalt();
        Assert.Equal(PinHasher.SaltSize, first.Length);
        Assert.Equal(PinHasher.SaltSize, second.Length);
        Assert.NotEqual(first, second);
    }
}