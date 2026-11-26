using System.Security.Cryptography;
using System.Text;

namespace Hesabyar.Application.Security;

public static class PinHasher
{
    public const int DefaultIterations = 600_000;
    public const int SaltSize = 16;
    public const int HashSize = 32;

    public static string NormalizePin(string pin)
    {
        ArgumentNullException.ThrowIfNull(pin);
        var builder = new StringBuilder(pin.Length);
        foreach (var character in pin)
            builder.Append(character switch
            {
                >= '\u06F0' and <= '\u06F9' => (char)('0' + character - '\u06F0'),
                >= '\u0660' and <= '\u0669' => (char)('0' + character - '\u0660'),
                _ => character
            });
        return builder.ToString();
    }

    public static bool IsValidPin(string? pin)
    {
        if (string.IsNullOrWhiteSpace(pin)) return false;
        var normalized = NormalizePin(pin);
        return normalized.Length is >= 4 and <= 8 &&
               normalized.All(character => character is >= '0' and <= '9');
    }

    public static byte[] CreateSalt() => RandomNumberGenerator.GetBytes(SaltSize);

    public static byte[] Hash(string pin, byte[] salt, int iterations = DefaultIterations)
    {
        ArgumentNullException.ThrowIfNull(salt);
        if (!IsValidPin(pin)) throw new ArgumentException("PIN must contain 4 to 8 digits.", nameof(pin));
        if (salt.Length < SaltSize) throw new ArgumentException("Salt is too short.", nameof(salt));
        if (iterations < DefaultIterations) throw new ArgumentOutOfRangeException(nameof(iterations));
        return Rfc2898DeriveBytes.Pbkdf2(NormalizePin(pin), salt, iterations, HashAlgorithmName.SHA256, HashSize);
    }

    public static bool Verify(string pin, byte[] salt, byte[] expectedHash, int iterations)
    {
        ArgumentNullException.ThrowIfNull(expectedHash);
        if (!IsValidPin(pin) || expectedHash.Length != HashSize) return false;
        var actualHash = Hash(pin, salt, iterations);
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}