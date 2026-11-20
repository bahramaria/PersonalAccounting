using System.Globalization;
using System.Text;
namespace Hesabyar.Localization.Numbers;
public sealed class NumberNormalizer : INumberNormalizer
{
    public string ToLatinDigits(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
            builder.Append(character switch
            {
                >= '۰' and <= '۹' => (char)('0' + character - '۰'),
                >= '٠' and <= '٩' => (char)('0' + character - '٠'),
                '٫' => '.',
                '٬' => ',',
                _ => character
            });
        return builder.ToString();
    }
    public string NormalizeText(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return ToLatinDigits(value).Replace('ي', 'ی').Replace('ك', 'ک').Replace("\u200c", string.Empty, StringComparison.Ordinal).Trim();
    }
    public bool TryParseAmount(string value, out decimal amount)
    {
        amount = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var normalized = ToLatinDigits(value).Replace("تومان", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("ریال", string.Empty, StringComparison.OrdinalIgnoreCase).Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace(",", string.Empty, StringComparison.Ordinal);
        return decimal.TryParse(normalized, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount);
    }
}