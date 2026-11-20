namespace Hesabyar.Localization.Numbers;
public interface INumberNormalizer
{
    string ToLatinDigits(string value);
    string NormalizeText(string value);
    bool TryParseAmount(string value, out decimal amount);
}