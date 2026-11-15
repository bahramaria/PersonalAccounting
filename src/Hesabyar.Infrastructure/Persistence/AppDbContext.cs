using Hesabyar.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Hesabyar.Infrastructure.Persistence;

public sealed class AppDbContext(
    DbContextOptions<AppDbContext> options,
    TimeProvider? timeProvider = null) : DbContext(options)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public DbSet<DbAccount> Accounts => Set<DbAccount>();
    public DbSet<DbLedgerTransaction> Transactions => Set<DbLedgerTransaction>();
    public DbSet<DbPosting> Postings => Set<DbPosting>();
    public DbSet<DbParty> Parties => Set<DbParty>();
    public DbSet<DbCheque> Cheques => Set<DbCheque>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureAccounts(modelBuilder);
        ConfigureTransactions(modelBuilder);
        ConfigurePostings(modelBuilder);
        ConfigureParties(modelBuilder);
        ConfigureCheques(modelBuilder);
    }

    public override int SaveChanges()
    {
        ApplyAuditFields();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuditFields();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void ApplyAuditFields()
    {
        var nowUtc = _timeProvider.GetUtcNow();
        foreach (var entry in ChangeTracker.Entries<DbEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.CreatedAtUtc == default)
                    entry.Entity.CreatedAtUtc = nowUtc;
                if (entry.Entity.UpdatedAtUtc == default)
                    entry.Entity.UpdatedAtUtc = nowUtc;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAtUtc = nowUtc;
            }
        }
    }

    private static void ConfigureAccounts(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<DbAccount>();
        entity.ToTable("Accounts", table =>
            table.HasCheckConstraint("CK_Accounts_CurrencyCode_Length", "length(CurrencyCode) = 3"));
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
        entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(16);
        entity.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
        entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
        entity.Property(x => x.SystemKey).HasMaxLength(32);
        entity.Property(x => x.BankName).HasMaxLength(80);
        entity.Property(x => x.CardLast4).HasMaxLength(4);
        entity.Property(x => x.AccountNumberMasked).HasMaxLength(40);
        entity.HasIndex(x => x.ParentId);
        entity.HasIndex(x => new { x.SystemKey, x.CurrencyCode }).IsUnique().HasFilter("\"SystemKey\" IS NOT NULL");
        entity.HasOne<DbAccount>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => !x.IsDeleted);
    }

    private static void ConfigureTransactions(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<DbLedgerTransaction>();
        entity.ToTable("Transactions");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.BusinessDate).HasColumnType("TEXT");
        entity.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
        entity.Property(x => x.Source).HasConversion<string>().HasMaxLength(16);
        entity.Property(x => x.Description).HasMaxLength(500);
        entity.Property(x => x.DueDate).HasColumnType("TEXT");
        entity.Property(x => x.SmsMessageId).HasMaxLength(160);
        entity.HasIndex(x => x.BusinessDate).IsDescending();
        entity.HasIndex(x => x.PartyId);
        entity.HasQueryFilter(x => !x.IsDeleted);
    }

    private static void ConfigureParties(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<DbParty>();
        entity.ToTable("Parties");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
        entity.Property(x => x.Phone).HasMaxLength(40);
        entity.Property(x => x.Note).HasMaxLength(500);
        entity.HasIndex(x => x.Name);
        entity.HasQueryFilter(x => !x.IsDeleted);
    }

    private static void ConfigureCheques(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<DbCheque>();
        entity.ToTable("Cheques", table => table.HasCheckConstraint("CK_Cheques_Amount_Positive", "AmountMinor > 0"));
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Direction).HasConversion<string>().HasMaxLength(12);
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
        entity.Property(x => x.BankName).HasMaxLength(80).IsRequired();
        entity.Property(x => x.SerialNumber).HasMaxLength(40).IsRequired();
        entity.Property(x => x.SayadNumber).HasMaxLength(24);
        entity.Property(x => x.DueDate).HasColumnType("TEXT");
        entity.Property(x => x.Note).HasMaxLength(500);
        entity.HasIndex(x => new { x.Direction, x.BankName, x.SerialNumber }).IsUnique().HasFilter("\"IsDeleted\" = 0");
        entity.HasIndex(x => x.SayadNumber).IsUnique().HasFilter("\"SayadNumber\" IS NOT NULL AND \"IsDeleted\" = 0");
        entity.HasIndex(x => new { x.IsArchived, x.DueDate });
        entity.HasQueryFilter(x => !x.IsDeleted);
    }

    private static void ConfigurePostings(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<DbPosting>();
        entity.ToTable("Postings", table =>
        {
            table.HasCheckConstraint("CK_Postings_CurrencyCode_Length", "length(CurrencyCode) = 3");
            table.HasCheckConstraint("CK_Postings_Amount_NonZero", "AmountMinor <> 0");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
        entity.Property(x => x.Memo).HasMaxLength(300);
        entity.HasIndex(x => new { x.AccountId, x.TransactionId });
        entity.HasIndex(x => x.PartyId);
        entity.HasOne(x => x.Transaction)
            .WithMany(x => x.Postings)
            .HasForeignKey(x => x.TransactionId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.HasOne(x => x.Account)
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => !x.IsDeleted);
    }
}
