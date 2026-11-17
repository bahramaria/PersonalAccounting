using Microsoft.EntityFrameworkCore;

namespace Hesabyar.Server.Persistence;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public DbSet<PhoneOtpChallengeEntity> PhoneOtpChallenges => Set<PhoneOtpChallengeEntity>();
    public DbSet<PhoneOtpRateLimitEntity> PhoneOtpRateLimits => Set<PhoneOtpRateLimitEntity>();
    public DbSet<IdentityUserEntity> Users => Set<IdentityUserEntity>();
    public DbSet<RefreshTokenSessionEntity> RefreshTokenSessions => Set<RefreshTokenSessionEntity>();
    public DbSet<SyncChangeLogEntity> SyncChangeLogs => Set<SyncChangeLogEntity>();

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

        var refresh = modelBuilder.Entity<RefreshTokenSessionEntity>();
        refresh.ToTable("RefreshTokenSessions");
        refresh.HasKey(x => x.Id);
        refresh.Property(x => x.DeviceName).HasMaxLength(80).IsRequired();
        refresh.Property(x => x.TokenHash).HasColumnType("varbinary(32)").IsRequired();
        refresh.HasIndex(x => x.TokenHash).IsUnique();
        refresh.HasIndex(x => new { x.UserId, x.DeviceId });
        refresh.HasIndex(x => x.FamilyId);
        refresh.HasIndex(x => x.ExpiresAtUtc);
        var sync = modelBuilder.Entity<SyncChangeLogEntity>();
        sync.ToTable("SyncChangeLogs");
        sync.HasKey(x => x.Id);
        sync.Property(x => x.AggregateType).HasMaxLength(64).IsRequired();
        sync.Property(x => x.Payload).IsRequired();
        sync.HasIndex(x => new { x.UserId, x.ChangeId }).IsUnique();
        sync.HasIndex(x => new { x.UserId, x.DeviceId, x.DeviceSequence }).IsUnique();
        sync.HasIndex(x => new { x.UserId, x.Id });
        sync.HasIndex(x => new { x.UserId, x.AggregateId, x.Id });
        sync.HasOne<IdentityUserEntity>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        refresh.HasOne<IdentityUserEntity>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
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
