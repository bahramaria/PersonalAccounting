using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Hesabyar.Server.Identity;

public sealed class AccessTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    System.Text.Encodings.Web.UrlEncoder encoder,
    byte[] signingSecret,
    TimeProvider timeProvider)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorization = Request.Headers["Authorization"].ToString();
        if (string.IsNullOrWhiteSpace(authorization))
            return Task.FromResult(AuthenticateResult.NoResult());

        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.Fail("Unsupported authorization scheme."));

        var parts = authorization["Bearer ".Length..].Trim().Split('.');
        if (parts.Length != 3 ||
            !TryDecode(parts[0], out var headerBytes) ||
            !TryDecode(parts[1], out var payloadBytes) ||
            !TryDecode(parts[2], out var suppliedSignature))
            return Task.FromResult(AuthenticateResult.Fail("Malformed access token."));

        var unsignedToken = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
        var expectedSignature = HMACSHA256.HashData(signingSecret, unsignedToken);
        if (suppliedSignature.Length != expectedSignature.Length ||
            !CryptographicOperations.FixedTimeEquals(suppliedSignature, expectedSignature))
            return Task.FromResult(AuthenticateResult.Fail("Invalid access-token signature."));

        try
        {
            using var header = JsonDocument.Parse(headerBytes);
            using var payload = JsonDocument.Parse(payloadBytes);
            var headerRoot = header.RootElement;
            var claims = payload.RootElement;

            if (!headerRoot.TryGetProperty("alg", out var algorithm) ||
                algorithm.GetString() != "HS256" ||
                !claims.TryGetProperty("iss", out var issuer) ||
                issuer.GetString() != "hesabyar" ||
                !claims.TryGetProperty("aud", out var audience) ||
                audience.GetString() != "hesabyar-client" ||
                !claims.TryGetProperty("sub", out var subject) ||
                !Guid.TryParseExact(subject.GetString(), "N", out var userId) ||
                userId == Guid.Empty ||
                !claims.TryGetProperty("phone", out var phone) ||
                string.IsNullOrWhiteSpace(phone.GetString()) ||
                !claims.TryGetProperty("iat", out var issuedAt) ||
                !issuedAt.TryGetInt64(out var issuedAtSeconds) ||
                !claims.TryGetProperty("exp", out var expiresAt) ||
                !expiresAt.TryGetInt64(out var expiresAtSeconds))
                return Task.FromResult(AuthenticateResult.Fail("Invalid access-token claims."));

            var now = timeProvider.GetUtcNow().ToUnixTimeSeconds();
            if (expiresAtSeconds <= now || issuedAtSeconds > now + 30 ||
                expiresAtSeconds <= issuedAtSeconds ||
                expiresAtSeconds - issuedAtSeconds > 3600)
                return Task.FromResult(AuthenticateResult.Fail("Expired or invalid access token."));

            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString("N")),
                new Claim(ClaimTypes.MobilePhone, phone.GetString()!)
            ], Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
        catch (JsonException)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid access-token payload."));
        }
        catch (InvalidOperationException)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid access-token claims."));
        }
    }

    private static bool TryDecode(string value, out byte[] bytes)
    {
        bytes = [];
        try
        {
            var normalized = value.Replace('-', '+').Replace('_', '/');
            normalized = normalized.PadRight(normalized.Length + (4 - normalized.Length % 4) % 4, '=');
            bytes = Convert.FromBase64String(normalized);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
