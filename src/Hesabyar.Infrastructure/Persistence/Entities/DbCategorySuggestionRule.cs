using Hesabyar.Domain.Ledger;

namespace Hesabyar.Infrastructure.Persistence.Entities;

public sealed class DbCategorySuggestionRule : DbEntity
{
    public required string MatchText { get; set; }
    public required string NormalizedMatchText { get; set; }
    public TransactionKind TransactionKind { get; set; }
    public Guid CategoryId { get; set; }
    public Guid? AccountId { get; set; }
    public bool IsEnabled { get; set; } = true;
    public int UsageCount { get; set; }
    public DateTimeOffset? LastUsedAtUtc { get; set; }
}
