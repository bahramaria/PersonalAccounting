using Microsoft.EntityFrameworkCore;

namespace Hesabyar.Infrastructure.Persistence;

public static class SqliteOptions
{
    public static DbContextOptions<AppDbContext> Create(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connectionString)
            .Options;
    }
}
