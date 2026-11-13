using Hesabyar.Domain.Accounts;

namespace Hesabyar.Infrastructure.Persistence.Entities;

public sealed class DbAccount : DbEntity
{
    public required string Name { get; set; }
    public AccountType Type { get; set; }
    public AccountKind Kind { get; set; }
    public Guid? ParentId { get; set; }
    public required string CurrencyCode { get; set; }
    public string? Icon { get; set; }
    public string? Color { get; set; }
    public int SortOrder { get; set; }
    public bool IsArchived { get; set; }
    public bool IncludeInTotal { get; set; } = true;
    public string? BankName { get; set; }
    public string? CardLast4 { get; set; }
    public string? AccountNumberMasked { get; set; }
}
