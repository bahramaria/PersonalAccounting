using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Hesabyar.Server.Identity;

public sealed record IssuedAccessToken(string Value, DateTimeOffset ExpiresAtUtc);

public sealed class PhoneOtpTokenIssuer
{
    private readonly byte[] _secret;
    private readonly TimeSpan _lifetime;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public PhoneOtpTokenIssuer(byte[] secret, TimeSpan lifetime)
    {
        ArgumentNullException.ThrowIfNull(secret);
        if (secret.Length < 32)
            throw new ArgumentException("The access-token signing secret must contain at least 32 bytes.", nameof(secret));
        if (lifetime <= TimeSpan.Zero || lifetime > TimeSpan.FromHours(1))
            throw new ArgumentOutOfRangeException(nameof(lifetime));
        _secret = secret.ToArray();
        _lifetime = lifetime;
    }

    public IssuedAccessToken Issue(Guid userId, string phoneNumber, DateTimeOffset? nowUtc = null)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("A user identifier is required.", nameof(userId));
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);

        var issuedAt = nowUtc ?? DateTimeOffset.UtcNow;
        var expiresAt = issuedAt.Add(_lifetime);
        var header = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(
            new { alg = "HS256", typ = "JWT" }, JsonOptions));
        var payload = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = "hesabyar",
            aud = "hesabyar-client",
            sub = userId.ToString("N"),
            phone = phoneNumber,
            iat = issuedAt.ToUnixTimeSeconds(),
            exp = expiresAt.ToUnixTimeSeconds(),
            jti = Guid.NewGuid().ToString("N")
        }, JsonOptions));
        var unsignedToken = $"{header}.{payload}";
        var signature = HMACSHA256.HashData(_secret, Encoding.ASCII.GetBytes(unsignedToken));
        return new IssuedAccessToken($"{unsignedToken}.{Base64UrlEncode(signature)}", expiresAt);
    }

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
