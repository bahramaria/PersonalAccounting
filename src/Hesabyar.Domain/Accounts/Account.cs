using Hesabyar.Domain.Common;
using Hesabyar.Domain.ValueObjects;
namespace Hesabyar.Domain.Accounts;
public sealed class Account : EntityBase
{
    public Account(string name, AccountType type, AccountKind kind, CurrencyCode currency, DateTimeOffset nowUtc) : base(nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim(); Type = type; Kind = kind; Currency = currency;
    }
    public string Name { get; private set; }
    public AccountType Type { get; }
    public AccountKind Kind { get; }
    public CurrencyCode Currency { get; }
    public bool IsArchived { get; private set; }
    public bool IncludeInTotal { get; private set; } = true;
    public void Rename(string name, DateTimeOffset nowUtc) { ArgumentException.ThrowIfNullOrWhiteSpace(name); Name = name.Trim(); MarkUpdated(nowUtc); }
    public void Archive(DateTimeOffset nowUtc) { IsArchived = true; MarkUpdated(nowUtc); }
    public void SetIncludedInTotal(bool included, DateTimeOffset nowUtc) { IncludeInTotal = included; MarkUpdated(nowUtc); }
}