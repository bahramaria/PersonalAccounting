namespace Hesabyar.Infrastructure.Persistence.Entities;

public sealed class DbSmsInboxItem : DbEntity
{
    public required string Sender { get; set; }
    public required string Body { get; set; }
    public required string DedupeHash { get; set; }
    public string? BankKey { get; set; }
    public string ParseStatus { get; set; } = "NeedsReview";
    public string TransactionType { get; set; } = "Unknown";
    public long? AmountMinor { get; set; }
    public long? BalanceMinor { get; set; }
    public string? CardLastFour { get; set; }
    public string? DateText { get; set; }
    public string? TransactionTime { get; set; }
    public string Status { get; set; } = "Pending";
    public Guid? TransactionId { get; set; }
}
