namespace Hesabyar.Infrastructure.Persistence.Entities;

public sealed class DbReminder : DbEntity
{
    public required string Title { get; set; }
    public required string Kind { get; set; }
    public DateOnly DueDate { get; set; }
    public DateTimeOffset? NotifyAtUtc { get; set; }
    public bool IsCompleted { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public Guid? RelatedEntityId { get; set; }
    public string? Note { get; set; }
}