namespace Hesabyar.Infrastructure.Persistence.Entities;

public sealed class DbParty : DbEntity
{
    public required string Name { get; set; }
    public string? Phone { get; set; }
    public string? Note { get; set; }
    public bool IsArchived { get; set; }
}
