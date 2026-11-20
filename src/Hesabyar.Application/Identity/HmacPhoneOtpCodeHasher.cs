using System.Security.Cryptography;
using System.Text;

namespace Hesabyar.Application.Identity;

public sealed class HmacPhoneOtpCodeHasher : IPhoneOtpCodeHasher
{
    private readonly byte[] _secret;

    public HmacPhoneOtpCodeHasher(byte[] secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        if (secret.Length < 32)
            throw new ArgumentException("The OTP HMAC secret must contain at least 32 bytes.", nameof(secret));

        _secret = secret.ToArray();
    }

    public byte[] ComputeHash(Guid challengeId, string phoneNumber, string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        var payload = Encoding.UTF8.GetBytes($"{challengeId:N}:{phoneNumber}:{code}");
        return HMACSHA256.HashData(_secret, payload);
    }
}
