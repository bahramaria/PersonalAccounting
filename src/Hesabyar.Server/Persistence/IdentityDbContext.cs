using Microsoft.EntityFrameworkCore;

namespace Hesabyar.Server.Persistence;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public DbSet<PhoneOtpChallengeEntity> PhoneOtpChallenges => Set<PhoneOtpChallengeEntity>();
    public DbSet<PhoneOtpRateLimitEntity> PhoneOtpRateLimits => Set<PhoneOtpRateLimitEntity>();
    public DbSet<IdentityUserEntity> Users => Set<IdentityUserEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var challenge = modelBuilder.Entity<PhoneOtpChallengeEntity>();
        challenge.ToTable("PhoneOtpChallenges");
        challenge.HasKey(x => x.Id);
        challenge.Property(x => x.PhoneNumber).HasMaxLength(16).IsRequired();
        challenge.Property(x => x.CodeHash).HasColumnType("varbinary(32)").IsRequired();
        challenge.HasIndex(x => new { x.PhoneNumber, x.CreatedAtUtc });
        challenge.HasIndex(x => x.ExpiresAtUtc);

        var rateLimit = modelBuilder.Entity<PhoneOtpRateLimitEntity>();
        rateLimit.ToTable("PhoneOtpRateLimits", table =>
            table.HasCheckConstraint("CK_PhoneOtpRateLimits_RequestCount_NonNegative", "[RequestCount] >= 0"));
        rateLimit.HasKey(x => x.PhoneNumber);
        rateLimit.Property(x => x.PhoneNumber).HasMaxLength(16);

        var user = modelBuilder.Entity<IdentityUserEntity>();
        user.ToTable("Users");
        user.HasKey(x => x.Id);
        user.Property(x => x.PhoneNumber).HasMaxLength(16).IsRequired();
        user.HasIndex(x => x.PhoneNumber).IsUnique();
    }
}

public sealed class PhoneOtpChallengeEntity
{
    public Guid Id { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public byte[] CodeHash { get; set; } = [];
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public int FailedAttempts { get; set; }
    public DateTimeOffset? ConsumedAtUtc { get; set; }
    public DateTimeOffset? CancelledAtUtc { get; set; }
}

public sealed class PhoneOtpRateLimitEntity
{
    public string PhoneNumber { get; set; } = string.Empty;
    public DateTimeOffset WindowStartedAtUtc { get; set; }
    public int RequestCount { get; set; }
    public DateTimeOffset LastRequestedAtUtc { get; set; }
}

public sealed class IdentityUserEntity
{
    public Guid Id { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public bool IsActive { get; set; }
}
