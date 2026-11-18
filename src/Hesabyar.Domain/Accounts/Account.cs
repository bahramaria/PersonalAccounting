using Hesabyar.Domain.Common;
using Hesabyar.Domain.ValueObjects;

namespace Hesabyar.Domain.Accounts;

public sealed class Account : EntityBase
{
    public Account(
        string name,
        AccountType type,
        AccountKind kind,
        CurrencyCode currency,
        DateTimeOffset nowUtc,
        Guid? parentId = null,
        string? icon = null,
        string? color = null,
        int sortOrder = 0,
        string? bankName = null,
        string? cardLast4 = null,
        string? accountNumberMasked = null) : base(nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        Type = type;
        Kind = kind;
        Currency = currency;
        ParentId = parentId;
        Icon = icon;
        Color = color;
        SortOrder = sortOrder;
        BankName = bankName;
        CardLast4 = cardLast4;
        AccountNumberMasked = accountNumberMasked;
    }

    public string Name { get; private set; }
    public AccountType Type { get; }
    public AccountKind Kind { get; }
    public CurrencyCode Currency { get; }
    public Guid? ParentId { get; }
    public string? Icon { get; }
    public string? Color { get; }
    public int SortOrder { get; }
    public string? BankName { get; }
    public string? CardLast4 { get; }
    public string? AccountNumberMasked { get; }
    public bool IsArchived { get; private set; }
    public bool IncludeInTotal { get; private set; } = true;

    public void Rename(string name, DateTimeOffset nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        MarkUpdated(nowUtc);
    }

    public void Archive(DateTimeOffset nowUtc)
    {
        IsArchived = true;
        MarkUpdated(nowUtc);
    }

    public void Restore(DateTimeOffset nowUtc)
    {
        IsArchived = false;
        MarkUpdated(nowUtc);
    }

    public void SetIncludedInTotal(bool included, DateTimeOffset nowUtc)
    {
        IncludeInTotal = included;
        MarkUpdated(nowUtc);
    }
}
