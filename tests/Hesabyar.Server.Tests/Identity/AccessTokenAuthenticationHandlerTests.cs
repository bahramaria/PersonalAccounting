using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Hesabyar.Server.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hesabyar.Server.Tests.Identity;

public sealed class AccessTokenAuthenticationHandlerTests
{
    private static readonly DateTimeOffset FixedNow =
        DateTimeOffset.Parse("2026-11-17T12:00:00Z");

    [Fact]
    public async Task AuthenticateAsync_AcceptsValidSignedToken()
    {
        var secret = RandomNumberGenerator.GetBytes(32);
        var userId = Guid.NewGuid();
        var token = CreateToken(secret, userId, FixedNow.AddMinutes(-1), FixedNow.AddMinutes(10));

        var result = await AuthenticateAsync(secret, token);

        Assert.True(result.Succeeded);
        Assert.Equal(userId.ToString("N"),
            result.Principal!.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        Assert.Equal("+989121234567",
            result.Principal.FindFirst(ClaimTypes.MobilePhone)!.Value);
    }

    [Fact]
    public async Task AuthenticateAsync_RejectsModifiedSignature()
    {
        var secret = RandomNumberGenerator.GetBytes(32);
        var token = CreateToken(secret, Guid.NewGuid(), FixedNow, FixedNow.AddMinutes(10));
        var parts = token.Split('.');
        var signature = Decode(parts[2]);
        signature[0] ^= 0x01;
        parts[2] = Encode(signature);

        var result = await AuthenticateAsync(secret, string.Join('.', parts));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task AuthenticateAsync_RejectsExpiredToken()
    {
        var secret = RandomNumberGenerator.GetBytes(32);
        var token = CreateToken(secret, Guid.NewGuid(), FixedNow.AddMinutes(-20), FixedNow.AddMinutes(-5));

        var result = await AuthenticateAsync(secret, token);

        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData("another-issuer", "hesabyar-client")]
    [InlineData("hesabyar", "another-client")]
    public async Task AuthenticateAsync_RejectsUnexpectedIssuerOrAudience(string issuer, string audience)
    {
        var secret = RandomNumberGenerator.GetBytes(32);
        var token = CreateToken(
            secret, Guid.NewGuid(), FixedNow.AddMinutes(-1), FixedNow.AddMinutes(10), issuer, audience);

        var result = await AuthenticateAsync(secret, token);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task AuthenticateAsync_RejectsUnsupportedAlgorithmEvenWithValidSignature()
    {
        var secret = RandomNumberGenerator.GetBytes(32);
        var token = CreateToken(
            secret, Guid.NewGuid(), FixedNow.AddMinutes(-1), FixedNow.AddMinutes(10), algorithm: "HS384");

        var result = await AuthenticateAsync(secret, token);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task AuthenticateAsync_RejectsTokenIssuedTooFarInFuture()
    {
        var secret = RandomNumberGenerator.GetBytes(32);
        var token = CreateToken(
            secret, Guid.NewGuid(), FixedNow.AddMinutes(2), FixedNow.AddMinutes(10));

        var result = await AuthenticateAsync(secret, token);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task AuthenticateAsync_RejectsTokenWithLifetimeOverOneHour()
    {
        var secret = RandomNumberGenerator.GetBytes(32);
        var token = CreateToken(
            secret, Guid.NewGuid(), FixedNow.AddMinutes(-1), FixedNow.AddHours(2));

        var result = await AuthenticateAsync(secret, token);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task AuthenticateAsync_RejectsEmptyUserIdentifier()
    {
        var secret = RandomNumberGenerator.GetBytes(32);
        var token = CreateToken(
            secret, Guid.Empty, FixedNow.AddMinutes(-1), FixedNow.AddMinutes(10));

        var result = await AuthenticateAsync(secret, token);

        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Bearer")]
    [InlineData("Bearer a.b")]
    [InlineData("Bearer a.b.c.d")]
    public async Task AuthenticateAsync_RejectsMalformedAuthorizationHeader(string authorization)
    {
        var secret = RandomNumberGenerator.GetBytes(32);

        var result = await AuthenticateAsync(secret, authorization, isRawAuthorization: true);

        Assert.False(result.Succeeded);
    }

    private static async Task<AuthenticateResult> AuthenticateAsync(
        byte[] secret, string token, bool isRawAuthorization = false)
    {
        var handler = new AccessTokenAuthenticationHandler(
            new TestOptionsMonitor<AuthenticationSchemeOptions>(new AuthenticationSchemeOptions()),
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            secret,
            new FixedTimeProvider(FixedNow));
        var context = new DefaultHttpContext();
        context.Request.Headers["Authorization"] = isRawAuthorization ? token : $"Bearer {token}";
        await handler.InitializeAsync(
            new AuthenticationScheme("Bearer", "Bearer", typeof(AccessTokenAuthenticationHandler)),
            context);
        return await handler.AuthenticateAsync();
    }

    private static string CreateToken(
        byte[] secret,
        Guid userId,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt,
        string issuer = "hesabyar",
        string audience = "hesabyar-client",
        string algorithm = "HS256")
    {
        var header = Encode(JsonSerializer.SerializeToUtf8Bytes(new { alg = algorithm, typ = "JWT" }));
        var payload = Encode(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = issuer,
            aud = audience,
            sub = userId.ToString("N"),
            phone = "+989121234567",
            iat = issuedAt.ToUnixTimeSeconds(),
            exp = expiresAt.ToUnixTimeSeconds()
        }));
        var unsigned = $"{header}.{payload}";
        var signature = HMACSHA256.HashData(secret, Encoding.ASCII.GetBytes(unsigned));
        return $"{unsigned}.{Encode(signature)}";
    }

    private static byte[] Decode(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized = normalized.PadRight(normalized.Length + (4 - normalized.Length % 4) % 4, '=');
        return Convert.FromBase64String(normalized);
    }

    private static string Encode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TestOptionsMonitor<TOptions>(TOptions value) : IOptionsMonitor<TOptions>
        where TOptions : class
    {
        public TOptions CurrentValue => value;
        public TOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<TOptions, string?> listener) => null;
    }
}
