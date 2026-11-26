namespace Hesabyar.Domain.ValueObjects;
public readonly record struct CurrencyCode
{
    public CurrencyCode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.Length != 3 || normalized.Any(c => !char.IsAsciiLetter(c))) throw new ArgumentException("Currency code must contain three ASCII letters.", nameof(value));
        Value = normalized;
    }
    public string Value { get; }
    public static CurrencyCode Irr { get; } = new("IRR");
    public override string ToString() => Value;
}