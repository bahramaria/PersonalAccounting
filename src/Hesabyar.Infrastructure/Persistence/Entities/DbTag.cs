namespace Hesabyar.Infrastructure.Persistence.Entities;

public sealed class DbTag : DbEntity
{
    public required string Name { get; set; }
    public string? Color { get; set; }
}