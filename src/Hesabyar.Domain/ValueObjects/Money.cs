namespace Hesabyar.Domain.ValueObjects;
public readonly record struct Money(long Minor, CurrencyCode Currency)
{
    public static Money Zero(CurrencyCode currency) => new(0, currency);
    public Money Add(Money other) { EnsureSameCurrency(other); return new(checked(Minor + other.Minor), Currency); }
    public Money Subtract(Money other) { EnsureSameCurrency(other); return new(checked(Minor - other.Minor), Currency); }
    public Money Negate() => new(checked(-Minor), Currency);
    public static Money operator +(Money left, Money right) => left.Add(right);
    public static Money operator -(Money left, Money right) => left.Subtract(right);
    private void EnsureSameCurrency(Money other) { if (Currency != other.Currency) throw new InvalidOperationException("Cannot combine different currencies."); }
}