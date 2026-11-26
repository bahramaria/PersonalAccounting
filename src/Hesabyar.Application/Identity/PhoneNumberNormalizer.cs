using System.Text;

namespace Hesabyar.Application.Identity;

public static class PhoneNumberNormalizer
{
    public static bool TryNormalizeE164(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character) || character is '-' or '(' or ')' or '.')
                continue;

            builder.Append(NormalizeDigit(character));
        }

        var candidate = builder.ToString();
        if (candidate.StartsWith("00", StringComparison.Ordinal))
            candidate = "+" + candidate[2..];

        if (candidate.Length is < 9 or > 16 || candidate[0] != '+' ||
            candidate[1] is < '1' or > '9')
            return false;

        for (var index = 1; index < candidate.Length; index++)
        {
            if (candidate[index] is < '0' or > '9')
                return false;
        }

        normalized = candidate;
        return true;
    }

    private static char NormalizeDigit(char value) => value switch
    {
        >= '\u06F0' and <= '\u06F9' => (char)('0' + value - '\u06F0'),
        >= '\u0660' and <= '\u0669' => (char)('0' + value - '\u0660'),
        _ => value
    };
}
