using Hesabyar.Domain.Accounts;
using Hesabyar.Domain.Ledger;
using Hesabyar.Infrastructure.Persistence;
using Hesabyar.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace Hesabyar.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
public sealed class AppDbContextModelSnapshot : ModelSnapshot
{
    public void BuildTargetModelForMigration(ModelBuilder modelBuilder) => BuildModel(modelBuilder);

    protected override void BuildModel(ModelBuilder modelBuilder)
    {
#pragma warning disable 612, 618
        modelBuilder.HasAnnotation("ProductVersion", "10.0.15")
            .HasAnnotation("Relational:MaxIdentifierLength", 64);

        modelBuilder.Entity<DbAccount>(entity =>
        {
            entity.ToTable("Accounts", table => table.HasCheckConstraint("CK_Accounts_CurrencyCode_Length", "length(CurrencyCode) = 3"));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnType("TEXT");
            entity.Property(x => x.Name).IsRequired().HasMaxLength(120);
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(16);
            entity.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
            entity.Property(x => x.ParentId).HasColumnType("TEXT");
            entity.Property(x => x.CurrencyCode).IsRequired().HasMaxLength(3);
            entity.Property(x => x.SystemKey).HasMaxLength(32);
            entity.Property(x => x.Icon);
            entity.Property(x => x.Color);
            entity.Property(x => x.SortOrder);
            entity.Property(x => x.IsArchived);
            entity.Property(x => x.IncludeInTotal);
            entity.Property(x => x.BankName).HasMaxLength(80);
            entity.Property(x => x.CardLast4).HasMaxLength(4);
            entity.Property(x => x.AccountNumberMasked).HasMaxLength(40);
            entity.Property(x => x.CreatedAtUtc).HasColumnType("TEXT");
            entity.Property(x => x.UpdatedAtUtc).HasColumnType("TEXT");
            entity.Property(x => x.IsDeleted);
            entity.HasIndex(x => x.ParentId);
            entity.HasIndex(x => new { x.SystemKey, x.CurrencyCode }).IsUnique().HasFilter("\"SystemKey\" IS NOT NULL");
            entity.HasOne<DbAccount>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<DbLedgerTransaction>(entity =>
        {
            entity.ToTable("Transactions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnType("TEXT");
            entity.Property(x => x.BusinessDate).HasColumnType("TEXT");
            entity.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.Property(x => x.PartyId).HasColumnType("TEXT");
            entity.Property(x => x.Source).HasConversion<string>().HasMaxLength(16);
            entity.Property(x => x.SmsMessageId).HasMaxLength(160);
            entity.Property(x => x.IsConfirmed);
            entity.Property(x => x.CreatedAtUtc).HasColumnType("TEXT");
            entity.Property(x => x.UpdatedAtUtc).HasColumnType("TEXT");
            entity.Property(x => x.IsDeleted);
            entity.HasIndex(x => x.BusinessDate).IsDescending();
            entity.HasIndex(x => x.PartyId);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<DbPosting>(entity =>
        {
            entity.ToTable("Postings", table =>
            {
                table.HasCheckConstraint("CK_Postings_CurrencyCode_Length", "length(CurrencyCode) = 3");
                table.HasCheckConstraint("CK_Postings_Amount_NonZero", "AmountMinor <> 0");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnType("TEXT");
            entity.Property(x => x.TransactionId).HasColumnType("TEXT");
            entity.Property(x => x.AccountId).HasColumnType("TEXT");
            entity.Property(x => x.AmountMinor);
            entity.Property(x => x.CurrencyCode).IsRequired().HasMaxLength(3);
            entity.Property(x => x.PartyId).HasColumnType("TEXT");
            entity.Property(x => x.Memo).HasMaxLength(300);
            entity.Property(x => x.CreatedAtUtc).HasColumnType("TEXT");
            entity.Property(x => x.UpdatedAtUtc).HasColumnType("TEXT");
            entity.Property(x => x.IsDeleted);
            entity.HasIndex(x => new { x.AccountId, x.TransactionId });
            entity.HasIndex(x => x.PartyId);
            entity.HasIndex(x => x.TransactionId);
            entity.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Transaction).WithMany(x => x.Postings).HasForeignKey(x => x.TransactionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });
#pragma warning restore 612, 618
    }
}
