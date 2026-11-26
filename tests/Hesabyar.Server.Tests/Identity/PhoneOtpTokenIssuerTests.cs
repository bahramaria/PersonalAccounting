using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hesabyar.Server.Identity;
using Xunit;

namespace Hesabyar.Server.Tests.Identity;

public sealed class PhoneOtpTokenIssuerTests
{
    [Fact]
    public void Issue_CreatesSignedShortLivedJwtWithExpectedIdentityClaims()
    {
        var secret = RandomNumberGenerator.GetBytes(32);
        var issuer = new PhoneOtpTokenIssuer(secret, TimeSpan.FromMinutes(15));
        var userId = Guid.NewGuid();
        var issuedAt = DateTimeOffset.Parse("2026-11-17T12:00:00Z");
        var result = issuer.Issue(userId, "+989121234567", issuedAt);
        var parts = result.Value.Split('.');

        Assert.Equal(3, parts.Length);
        using var header = JsonDocument.Parse(Decode(parts[0]));
        using var payload = JsonDocument.Parse(Decode(parts[1]));
        Assert.Equal("HS256", header.RootElement.GetProperty("alg").GetString());
        Assert.Equal(userId.ToString("N"), payload.RootElement.GetProperty("sub").GetString());
        Assert.Equal("+989121234567", payload.RootElement.GetProperty("phone").GetString());
        Assert.Equal("hesabyar", payload.RootElement.GetProperty("iss").GetString());
        Assert.Equal("hesabyar-client", payload.RootElement.GetProperty("aud").GetString());
        Assert.Equal(issuedAt.AddMinutes(15), result.ExpiresAtUtc);

        var unsigned = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
        var expectedSignature = HMACSHA256.HashData(secret, unsigned);
        Assert.Equal(expectedSignature, Decode(parts[2]));
    }

    [Fact]
    public void Constructor_RejectsWeakSigningSecret() =>
        Assert.Throws<ArgumentException>(() =>
            new PhoneOtpTokenIssuer(new byte[31], TimeSpan.FromMinutes(15)));

    [Fact]
    public void Constructor_RejectsLongTokenLifetime() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PhoneOtpTokenIssuer(new byte[32], TimeSpan.FromHours(2)));

    private static byte[] Decode(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized = normalized.PadRight(normalized.Length + (4 - normalized.Length % 4) % 4, '=');
        return Convert.FromBase64String(normalized);
    }
}
