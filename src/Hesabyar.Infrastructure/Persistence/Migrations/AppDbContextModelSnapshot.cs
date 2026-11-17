using Hesabyar.Domain.Accounts;
using Hesabyar.Application.Cheques;
using Hesabyar.Application.Loans;
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

        modelBuilder.Entity<DbParty>(entity =>
        {
            entity.ToTable("Parties");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnType("TEXT");
            entity.Property(x => x.Name).IsRequired().HasMaxLength(120);
            entity.Property(x => x.Phone).HasMaxLength(40);
            entity.Property(x => x.Note).HasMaxLength(500);
            entity.Property(x => x.IsArchived);
            entity.Property(x => x.CreatedAtUtc).HasColumnType("TEXT");
            entity.Property(x => x.UpdatedAtUtc).HasColumnType("TEXT");
            entity.Property(x => x.IsDeleted);
            entity.HasIndex(x => x.Name);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<DbCheque>(entity =>
        {
            entity.ToTable("Cheques", table => table.HasCheckConstraint("CK_Cheques_Amount_Positive", "AmountMinor > 0"));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnType("TEXT");
            entity.Property(x => x.Direction).HasConversion<string>().HasMaxLength(12);
            entity.Property(x => x.BankName).IsRequired().HasMaxLength(80);
            entity.Property(x => x.SerialNumber).IsRequired().HasMaxLength(40);
            entity.Property(x => x.SayadNumber).HasMaxLength(24);
            entity.Property(x => x.DueDate).HasColumnType("TEXT");
            entity.Property(x => x.PersonId).HasColumnType("TEXT");
            entity.Property(x => x.AmountMinor);
            entity.Property(x => x.Note).HasMaxLength(500);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
            entity.Property(x => x.IsArchived);
            entity.Property(x => x.SettlementAccountId).HasColumnType("TEXT");
            entity.Property(x => x.SettlementTransactionId).HasColumnType("TEXT");
            entity.Property(x => x.CreatedAtUtc).HasColumnType("TEXT");
            entity.Property(x => x.UpdatedAtUtc).HasColumnType("TEXT");
            entity.Property(x => x.IsDeleted);
            entity.HasIndex(x => new { x.Direction, x.BankName, x.SerialNumber }).IsUnique().HasFilter("\"IsDeleted\" = 0");
            entity.HasIndex(x => x.SayadNumber).IsUnique().HasFilter("\"SayadNumber\" IS NOT NULL AND \"IsDeleted\" = 0");
            entity.HasIndex(x => new { x.IsArchived, x.DueDate });
            entity.HasQueryFilter(x => !x.IsDeleted);
        });


        modelBuilder.Entity<DbLoan>(entity =>
        {
            entity.ToTable("Loans", table =>
            {
                table.HasCheckConstraint("CK_Loans_Principal_Positive", "PrincipalMinor > 0");
                table.HasCheckConstraint("CK_Loans_AnnualRate_NonNegative", "AnnualRateBasisPoints >= 0");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnType("TEXT");
            entity.Property(x => x.Name).IsRequired().HasMaxLength(120);
            entity.Property(x => x.Direction).HasConversion<string>().HasMaxLength(12);
            entity.Property(x => x.ScheduleType).HasConversion<string>().HasMaxLength(12);
            entity.Property(x => x.PrincipalMinor);
            entity.Property(x => x.AnnualRateBasisPoints);
            entity.Property(x => x.StartDate).HasColumnType("TEXT");
            entity.Property(x => x.AccountId).HasColumnType("TEXT");
            entity.Property(x => x.LoanAccountId).HasColumnType("TEXT");
            entity.Property(x => x.PersonId).HasColumnType("TEXT");
            entity.Property(x => x.Note).HasMaxLength(500);
            entity.Property(x => x.IsClosed);
            entity.Property(x => x.CreatedAtUtc).HasColumnType("TEXT");
            entity.Property(x => x.UpdatedAtUtc).HasColumnType("TEXT");
            entity.Property(x => x.IsDeleted);
            entity.HasIndex(x => x.AccountId);
            entity.HasIndex(x => x.LoanAccountId);
            entity.HasIndex(x => x.PersonId);
            entity.HasIndex(x => x.StartDate);
            entity.HasOne<DbAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<DbAccount>().WithMany().HasForeignKey(x => x.LoanAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<DbParty>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(x => x.Installments).WithOne().HasForeignKey(x => x.LoanId).OnDelete(DeleteBehavior.Cascade);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<DbLoanInstallment>(entity =>
        {
            entity.ToTable("LoanInstallments", table =>
            {
                table.HasCheckConstraint("CK_LoanInstallments_Principal_NonNegative", "PrincipalDueMinor > 0");
                table.HasCheckConstraint("CK_LoanInstallments_Interest_NonNegative", "InterestDueMinor >= 0");
                table.HasCheckConstraint("CK_LoanInstallments_PaidPrincipal_NonNegative", "PaidPrincipalMinor >= 0 AND PaidPrincipalMinor <= PrincipalDueMinor");
                table.HasCheckConstraint("CK_LoanInstallments_PaidInterest_NonNegative", "PaidInterestMinor >= 0 AND PaidInterestMinor <= InterestDueMinor");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnType("TEXT");
            entity.Property(x => x.LoanId).HasColumnType("TEXT");
            entity.Property(x => x.DueDate).HasColumnType("TEXT");
            entity.Property(x => x.Sequence);
            entity.Property(x => x.PrincipalDueMinor);
            entity.Property(x => x.InterestDueMinor);
            entity.Property(x => x.PaidPrincipalMinor);
            entity.Property(x => x.PaidInterestMinor);
            entity.Property(x => x.CreatedAtUtc).HasColumnType("TEXT");
            entity.Property(x => x.UpdatedAtUtc).HasColumnType("TEXT");
            entity.Property(x => x.IsDeleted);
            entity.HasIndex(x => new { x.LoanId, x.DueDate, x.Sequence }).IsUnique();
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<DbSmsInboxItem>(entity =>
        {
            entity.ToTable("SmsInboxItems");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnType("TEXT");
            entity.Property(x => x.Sender).IsRequired().HasMaxLength(120);
            entity.Property(x => x.Body).IsRequired().HasMaxLength(4000);
            entity.Property(x => x.DedupeHash).IsRequired().HasMaxLength(64);
            entity.Property(x => x.BankKey).HasMaxLength(40);
            entity.Property(x => x.ParseStatus).IsRequired().HasMaxLength(20);
            entity.Property(x => x.TransactionType).IsRequired().HasMaxLength(20);
            entity.Property(x => x.AmountMinor);
            entity.Property(x => x.AmountUnitKnown);
            entity.Property(x => x.BalanceMinor);
            entity.Property(x => x.CardLastFour).HasMaxLength(4);
            entity.Property(x => x.DateText).HasMaxLength(20);
            entity.Property(x => x.TransactionTime).HasMaxLength(5);
            entity.Property(x => x.Status).IsRequired().HasMaxLength(20);
            entity.Property(x => x.TransactionId).HasColumnType("TEXT");
            entity.Property(x => x.BalanceAdjustmentTransactionId).HasColumnType("TEXT");
            entity.Property(x => x.CreatedAtUtc).HasColumnType("TEXT");
            entity.Property(x => x.UpdatedAtUtc).HasColumnType("TEXT");
            entity.Property(x => x.IsDeleted);
            entity.HasIndex(x => x.DedupeHash).IsUnique();
            entity.HasIndex(x => new { x.Status, x.CreatedAtUtc });
            entity.HasQueryFilter(x => !x.IsDeleted);
        });


        modelBuilder.Entity<DbReminder>(entity =>
        {
            entity.ToTable("Reminders");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnType("TEXT");
            entity.Property(x => x.Title).IsRequired().HasMaxLength(160);
            entity.Property(x => x.Kind).IsRequired().HasMaxLength(24);
            entity.Property(x => x.DueDate).HasColumnType("TEXT");
            entity.Property(x => x.NotifyAtUtc).HasColumnType("TEXT");
            entity.Property(x => x.IsCompleted);
            entity.Property(x => x.CompletedAtUtc).HasColumnType("TEXT");
            entity.Property(x => x.RelatedEntityId).HasColumnType("TEXT");
            entity.Property(x => x.Note).HasMaxLength(500);
            entity.Property(x => x.CreatedAtUtc).HasColumnType("TEXT");
            entity.Property(x => x.UpdatedAtUtc).HasColumnType("TEXT");
            entity.Property(x => x.IsDeleted);
            entity.HasIndex(x => new { x.IsCompleted, x.DueDate });
            entity.HasIndex(x => x.NotifyAtUtc);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<DbRecurringTransaction>(entity =>
        {
            entity.ToTable("RecurringTransactions", table =>
            {
                table.HasCheckConstraint("CK_RecurringTransactions_Amount_Positive", "AmountMinor > 0");
                table.HasCheckConstraint("CK_RecurringTransactions_Interval_Positive", "IntervalCount > 0");
                table.HasCheckConstraint("CK_RecurringTransactions_CurrencyCode_Length", "length(CurrencyCode) = 3");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnType("TEXT");
            entity.Property(x => x.Title).IsRequired().HasMaxLength(160);
            entity.Property(x => x.Kind).IsRequired().HasMaxLength(20);
            entity.Property(x => x.CurrencyCode).IsRequired().HasMaxLength(3);
            entity.Property(x => x.AmountMinor);
            entity.Property(x => x.AccountId).HasColumnType("TEXT");
            entity.Property(x => x.CategoryAccountId).HasColumnType("TEXT");
            entity.Property(x => x.StartDate).HasColumnType("TEXT");
            entity.Property(x => x.NextOccurrenceDate).HasColumnType("TEXT");
            entity.Property(x => x.EndDate).HasColumnType("TEXT");
            entity.Property(x => x.IntervalCount);
            entity.Property(x => x.IntervalUnit).IsRequired().HasMaxLength(12);
            entity.Property(x => x.IsPaused);
            entity.Property(x => x.LastGeneratedAtUtc).HasColumnType("TEXT");
            entity.Property(x => x.CreatedAtUtc).HasColumnType("TEXT");
            entity.Property(x => x.UpdatedAtUtc).HasColumnType("TEXT");
            entity.Property(x => x.IsDeleted);
            entity.HasIndex(x => x.AccountId);
            entity.HasIndex(x => x.CategoryAccountId);
            entity.HasIndex(x => new { x.IsPaused, x.NextOccurrenceDate });
            entity.HasOne<DbAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<DbAccount>().WithMany().HasForeignKey(x => x.CategoryAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<DbRecurringTransactionOccurrence>(entity =>
        {
            entity.ToTable("RecurringTransactionOccurrences");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnType("TEXT");
            entity.Property(x => x.RecurringTransactionId).HasColumnType("TEXT");
            entity.Property(x => x.OccurrenceDate).HasColumnType("TEXT");
            entity.Property(x => x.TransactionId).HasColumnType("TEXT");
            entity.Property(x => x.Status).IsRequired().HasMaxLength(20);
            entity.Property(x => x.GeneratedAtUtc).HasColumnType("TEXT");
            entity.Property(x => x.CreatedAtUtc).HasColumnType("TEXT");
            entity.Property(x => x.UpdatedAtUtc).HasColumnType("TEXT");
            entity.Property(x => x.IsDeleted);
            entity.HasIndex(x => new { x.RecurringTransactionId, x.OccurrenceDate }).IsUnique()
                .HasDatabaseName("IX_RecurringTransactionOccurrences_RecurringTransactionId_OccurrenceDate");
            entity.HasIndex(x => x.TransactionId).IsUnique().HasFilter("\"TransactionId\" IS NOT NULL");
            entity.HasOne<DbRecurringTransaction>().WithMany().HasForeignKey(x => x.RecurringTransactionId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_RecurringTransactionOccurrences_RecurringTransactions_RecurringTransactionId");
            entity.HasOne<DbLedgerTransaction>().WithMany().HasForeignKey(x => x.TransactionId).OnDelete(DeleteBehavior.Restrict);
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
            entity.Property(x => x.DueDate).HasColumnType("TEXT");
            entity.Property(x => x.PartyId).HasColumnType("TEXT");
            entity.Property(x => x.LoanId).HasColumnType("TEXT");
            entity.Property(x => x.Source).HasConversion<string>().HasMaxLength(16);
            entity.Property(x => x.SmsMessageId).HasMaxLength(160);
            entity.Property(x => x.FiscalPeriodId).HasColumnType("TEXT");
            entity.Property(x => x.DocumentNumber).HasMaxLength(32);
            entity.Property(x => x.CounterAmountMinor);
            entity.Property(x => x.IsConfirmed);
            entity.Property(x => x.CreatedAtUtc).HasColumnType("TEXT");
            entity.Property(x => x.UpdatedAtUtc).HasColumnType("TEXT");
            entity.Property(x => x.IsDeleted);
            entity.HasIndex(x => x.BusinessDate).IsDescending();
            entity.HasIndex(x => x.DocumentNumber).IsUnique().HasFilter("\\\"DocumentNumber\\\" IS NOT NULL");
            entity.HasIndex(x => x.PartyId);
            entity.HasIndex(x => x.LoanId);
            entity.HasOne<DbLoan>().WithMany().HasForeignKey(x => x.LoanId).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });


        modelBuilder.Entity<DbBudget>(entity =>
        {
            entity.ToTable("Budgets", table =>
            {
                table.HasCheckConstraint("CK_Budgets_Limit_Positive", "LimitMinor > 0");
                table.HasCheckConstraint("CK_Budgets_Period_Valid", "PeriodEnd >= PeriodStart");
                table.HasCheckConstraint("CK_Budgets_Currency_Length", "length(CurrencyCode) = 3");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnType("TEXT");
            entity.Property(x => x.CategoryAccountId).HasColumnType("TEXT");
            entity.Property(x => x.PeriodStart).HasColumnType("TEXT");
            entity.Property(x => x.PeriodEnd).HasColumnType("TEXT");
            entity.Property(x => x.LimitMinor);
            entity.Property(x => x.CurrencyCode).IsRequired().HasMaxLength(3);
            entity.Property(x => x.IsActive);
            entity.Property(x => x.Note).HasMaxLength(300);
            entity.Property(x => x.CreatedAtUtc).HasColumnType("TEXT");
            entity.Property(x => x.UpdatedAtUtc).HasColumnType("TEXT");
            entity.Property(x => x.IsDeleted);
            entity.HasIndex(x => new { x.CategoryAccountId, x.PeriodStart, x.PeriodEnd }).IsUnique();
            entity.HasOne(x => x.CategoryAccount).WithMany().HasForeignKey(x => x.CategoryAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<DbTag>(entity =>
        {
            entity.ToTable("Tags");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnType("TEXT");
            entity.Property(x => x.Name).IsRequired().HasMaxLength(40);
            entity.Property(x => x.Color).HasMaxLength(16);
            entity.Property(x => x.CreatedAtUtc).HasColumnType("TEXT");
            entity.Property(x => x.UpdatedAtUtc).HasColumnType("TEXT");
            entity.Property(x => x.IsDeleted);
            entity.HasIndex(x => x.Name).IsUnique().HasFilter("\"IsDeleted\" = 0");
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<DbTransactionTag>(entity =>
        {
            entity.ToTable("TransactionTags");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnType("TEXT");
            entity.Property(x => x.TransactionId).HasColumnType("TEXT");
            entity.Property(x => x.TagId).HasColumnType("TEXT");
            entity.Property(x => x.CreatedAtUtc).HasColumnType("TEXT");
            entity.Property(x => x.UpdatedAtUtc).HasColumnType("TEXT");
            entity.Property(x => x.IsDeleted);
            entity.HasIndex(x => new { x.TransactionId, x.TagId }).IsUnique();
            entity.HasIndex(x => x.TagId);
            entity.HasOne<DbLedgerTransaction>().WithMany().HasForeignKey(x => x.TransactionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<DbTag>().WithMany().HasForeignKey(x => x.TagId).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<DbFiscalPeriod>(entity =>
        {
            entity.ToTable("FiscalPeriods", table => table.HasCheckConstraint("CK_FiscalPeriods_DateRange", "EndDate >= StartDate"));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnType("TEXT");
            entity.Property(x => x.Name).IsRequired().HasMaxLength(80);
            entity.Property(x => x.StartDate).HasColumnType("TEXT");
            entity.Property(x => x.EndDate).HasColumnType("TEXT");
            entity.Property(x => x.IsClosed);
            entity.Property(x => x.CreatedAtUtc).HasColumnType("TEXT");
            entity.Property(x => x.UpdatedAtUtc).HasColumnType("TEXT");
            entity.Property(x => x.IsDeleted);
            entity.HasIndex(x => x.StartDate).IsUnique();
            entity.HasIndex(x => new { x.StartDate, x.EndDate });
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<DbDocumentSequence>(entity =>
        {
            entity.ToTable("DocumentSequences", table => table.HasCheckConstraint("CK_DocumentSequences_LastNumber_NonNegative", "LastNumber >= 0"));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnType("TEXT");
            entity.Property(x => x.FiscalPeriodId).HasColumnType("TEXT");
            entity.Property(x => x.LastNumber);
            entity.Property(x => x.CreatedAtUtc).HasColumnType("TEXT");
            entity.Property(x => x.UpdatedAtUtc).HasColumnType("TEXT");
            entity.Property(x => x.IsDeleted);
            entity.HasIndex(x => x.FiscalPeriodId).IsUnique();
            entity.HasOne<DbFiscalPeriod>().WithMany().HasForeignKey(x => x.FiscalPeriodId).OnDelete(DeleteBehavior.Cascade);
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
