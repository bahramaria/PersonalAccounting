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
    [Fact]
    public async Task AuthenticateAsync_AcceptsValidSignedToken()
    {
        var secret = RandomNumberGenerator.GetBytes(32);
        var userId = Guid.NewGuid();
        var token = CreateToken(secret, userId, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(10));
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
        var token = CreateToken(secret, Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(10));
        var parts = token.Split('.');
        parts[2] = parts[2][..^1] + (parts[2][^1] == 'A' ? 'B' : 'A');

        var result = await AuthenticateAsync(secret, string.Join('.', parts));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task AuthenticateAsync_RejectsExpiredToken()
    {
        var secret = RandomNumberGenerator.GetBytes(32);
        var token = CreateToken(secret, Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-20), DateTimeOffset.UtcNow.AddMinutes(-5));

        var result = await AuthenticateAsync(secret, token);

        Assert.False(result.Succeeded);
    }

    private static async Task<AuthenticateResult> AuthenticateAsync(byte[] secret, string token)
    {
        var handler = new AccessTokenAuthenticationHandler(
            new TestOptionsMonitor<AuthenticationSchemeOptions>(new AuthenticationSchemeOptions()),
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            secret,
            TimeProvider.System);
        var context = new DefaultHttpContext();
        context.Request.Headers["Authorization"] = $"Bearer {token}";
        await handler.InitializeAsync(
            new AuthenticationScheme("Bearer", "Bearer", typeof(AccessTokenAuthenticationHandler)),
            context);
        return await handler.AuthenticateAsync();
    }

    private static string CreateToken(
        byte[] secret, Guid userId, DateTimeOffset issuedAt, DateTimeOffset expiresAt)
    {
        var header = Encode(JsonSerializer.SerializeToUtf8Bytes(new { alg = "HS256", typ = "JWT" }));
        var payload = Encode(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = "hesabyar",
            aud = "hesabyar-client",
            sub = userId.ToString("N"),
            phone = "+989121234567",
            iat = issuedAt.ToUnixTimeSeconds(),
            exp = expiresAt.ToUnixTimeSeconds()
        }));
        var unsigned = $"{header}.{payload}";
        var signature = HMACSHA256.HashData(secret, Encoding.ASCII.GetBytes(unsigned));
        return $"{unsigned}.{Encode(signature)}";
    }

    private static string Encode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class TestOptionsMonitor<TOptions>(TOptions value) : IOptionsMonitor<TOptions>
        where TOptions : class
    {
        public TOptions CurrentValue => value;
        public TOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<TOptions, string?> listener) => null;
    }
}
