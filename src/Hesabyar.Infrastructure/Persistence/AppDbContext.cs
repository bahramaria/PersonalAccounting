using System.Text.Json;
using Hesabyar.Sync.Contracts;
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
    public DbSet<DbLoan> Loans => Set<DbLoan>();
    public DbSet<DbLoanInstallment> LoanInstallments => Set<DbLoanInstallment>();
    public DbSet<DbSmsInboxItem> SmsInboxItems => Set<DbSmsInboxItem>();
    public DbSet<DbReminder> Reminders => Set<DbReminder>();
    public DbSet<DbRecurringTransaction> RecurringTransactions => Set<DbRecurringTransaction>();
    public DbSet<DbRecurringTransactionOccurrence> RecurringTransactionOccurrences => Set<DbRecurringTransactionOccurrence>();
    public DbSet<DbBudget> Budgets => Set<DbBudget>();
    public DbSet<DbTag> Tags => Set<DbTag>();
    public DbSet<DbTransactionTag> TransactionTags => Set<DbTransactionTag>();
    public DbSet<DbFiscalPeriod> FiscalPeriods => Set<DbFiscalPeriod>();
    public DbSet<DbDocumentSequence> DocumentSequences => Set<DbDocumentSequence>();
    public DbSet<DbSyncOutboxItem> SyncOutbox => Set<DbSyncOutboxItem>();
    public DbSet<DbPhysicalAsset> PhysicalAssets => Set<DbPhysicalAsset>();
    public DbSet<DbAssetValuation> AssetValuations => Set<DbAssetValuation>();
    public DbSet<DbDocumentAttachment> DocumentAttachments => Set<DbDocumentAttachment>();
    public DbSet<DbCategorySuggestionRule> CategorySuggestionRules => Set<DbCategorySuggestionRule>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureAccounts(modelBuilder);
        ConfigureTransactions(modelBuilder);
        ConfigurePostings(modelBuilder);
        ConfigureParties(modelBuilder);
        ConfigureCheques(modelBuilder);
        ConfigureLoans(modelBuilder);
        ConfigureSmsInbox(modelBuilder);
        ConfigureReminders(modelBuilder);
        ConfigureRecurringTransactions(modelBuilder);
        ConfigurePlanningAndFiscalFeatures(modelBuilder);
        ConfigureSyncOutbox(modelBuilder);
        ConfigurePhysicalAssets(modelBuilder);
        ConfigureDocumentAttachments(modelBuilder);
        ConfigureCategorySuggestionRules(modelBuilder);
    }

    public override int SaveChanges()
    {
        var ownsTransaction = Database.CurrentTransaction is null;
        using var transaction = ownsTransaction ? Database.BeginTransaction() : null;
        AssignDocumentNumbersAsync(CancellationToken.None).GetAwaiter().GetResult();
        PrepareSyncOutbox();
        ApplyAuditFields();
        var result = base.SaveChanges();
        transaction?.Commit();
        return result;
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var ownsTransaction = Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction
            ? await Database.BeginTransactionAsync(cancellationToken)
            : null;
        await AssignDocumentNumbersAsync(cancellationToken);
        await PrepareSyncOutboxAsync(cancellationToken);
        ApplyAuditFields();
        var result = await base.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private async Task AssignDocumentNumbersAsync(CancellationToken cancellationToken)
    {
        var pending = ChangeTracker.Entries<DbLedgerTransaction>()
            .Where(x => x.State == EntityState.Added && x.Entity.DocumentNumber is null)
            .Select(x => x.Entity)
            .ToArray();
        foreach (var item in pending)
        {
            var period = await FiscalPeriods.SingleOrDefaultAsync(
                x => x.StartDate <= item.BusinessDate && x.EndDate >= item.BusinessDate,
                cancellationToken);
            if (period is null)
                continue;
            if (period.IsClosed)
                throw new InvalidOperationException("Transactions cannot be added to a closed fiscal period.");

            var sequence = await DocumentSequences.SingleOrDefaultAsync(
                x => x.FiscalPeriodId == period.Id, cancellationToken);
            if (sequence is null)
            {
                sequence = new DbDocumentSequence
                {
                    Id = Guid.CreateVersion7(),
                    FiscalPeriodId = period.Id,
                    LastNumber = 0
                };
                DocumentSequences.Add(sequence);
            }

            sequence.LastNumber = checked(sequence.LastNumber + 1);
            item.FiscalPeriodId = period.Id;
            item.DocumentNumber = $"{period.Name}-{sequence.LastNumber:000000}";
        }
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
        entity.Property(x => x.DocumentNumber).HasMaxLength(32);
        entity.Property(x => x.CounterAmountMinor);
        entity.HasIndex(x => x.BusinessDate).IsDescending();
        entity.HasIndex(x => x.DocumentNumber).IsUnique().HasFilter("\"DocumentNumber\" IS NOT NULL");
        entity.HasIndex(x => x.PartyId);
        entity.HasIndex(x => x.LoanId);
        entity.HasOne<DbLoan>().WithMany().HasForeignKey(x => x.LoanId).OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => !x.IsDeleted);
    }

    private static void ConfigureSmsInbox(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<DbSmsInboxItem>();
        entity.ToTable("SmsInboxItems");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Sender).HasMaxLength(120).IsRequired();
        entity.Property(x => x.Body).HasMaxLength(4000).IsRequired();
        entity.Property(x => x.DedupeHash).HasMaxLength(64).IsRequired();
        entity.Property(x => x.BankKey).HasMaxLength(40);
        entity.Property(x => x.ParseStatus).HasMaxLength(20).IsRequired();
        entity.Property(x => x.TransactionType).HasMaxLength(20).IsRequired();
        entity.Property(x => x.CardLastFour).HasMaxLength(4);
        entity.Property(x => x.DateText).HasMaxLength(20);
        entity.Property(x => x.TransactionTime).HasMaxLength(5);
        entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
        entity.HasIndex(x => x.DedupeHash).IsUnique();
        entity.HasIndex(x => new { x.Status, x.CreatedAtUtc });
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
        entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
        entity.HasIndex(x => new { x.Direction, x.BankName, x.SerialNumber }).IsUnique().HasFilter("\"IsDeleted\" = 0");
        entity.HasIndex(x => x.SayadNumber).IsUnique().HasFilter("\"SayadNumber\" IS NOT NULL AND \"IsDeleted\" = 0");
        entity.HasIndex(x => new { x.IsArchived, x.DueDate });
        entity.HasQueryFilter(x => !x.IsDeleted);
    }


    private static void ConfigureLoans(ModelBuilder modelBuilder)
    {
        var loan = modelBuilder.Entity<DbLoan>();
        loan.ToTable("Loans", table =>
        {
            table.HasCheckConstraint("CK_Loans_Principal_Positive", "PrincipalMinor > 0");
            table.HasCheckConstraint("CK_Loans_AnnualRate_NonNegative", "AnnualRateBasisPoints >= 0");
        });
        loan.HasKey(x => x.Id);
        loan.Property(x => x.Name).HasMaxLength(120).IsRequired();
        loan.Property(x => x.Direction).HasConversion<string>().HasMaxLength(12);
        loan.Property(x => x.ScheduleType).HasConversion<string>().HasMaxLength(12);
        loan.Property(x => x.StartDate).HasColumnType("TEXT");
        loan.Property(x => x.Note).HasMaxLength(500);
        loan.HasIndex(x => x.StartDate);
        loan.HasOne<DbAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        loan.HasOne<DbAccount>().WithMany().HasForeignKey(x => x.LoanAccountId).OnDelete(DeleteBehavior.Restrict);
        loan.HasOne<DbParty>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Restrict);
        loan.HasMany(x => x.Installments).WithOne().HasForeignKey(x => x.LoanId).OnDelete(DeleteBehavior.Cascade);
        loan.HasQueryFilter(x => !x.IsDeleted);

        var installment = modelBuilder.Entity<DbLoanInstallment>();
        installment.ToTable("LoanInstallments", table =>
        {
            table.HasCheckConstraint("CK_LoanInstallments_Principal_NonNegative", "PrincipalDueMinor > 0");
            table.HasCheckConstraint("CK_LoanInstallments_Interest_NonNegative", "InterestDueMinor >= 0");
            table.HasCheckConstraint("CK_LoanInstallments_PaidPrincipal_NonNegative", "PaidPrincipalMinor >= 0 AND PaidPrincipalMinor <= PrincipalDueMinor");
            table.HasCheckConstraint("CK_LoanInstallments_PaidInterest_NonNegative", "PaidInterestMinor >= 0 AND PaidInterestMinor <= InterestDueMinor");
        });
        installment.HasKey(x => x.Id);
        installment.Property(x => x.DueDate).HasColumnType("TEXT");
        installment.HasIndex(x => new { x.LoanId, x.DueDate, x.Sequence }).IsUnique();
        installment.HasQueryFilter(x => !x.IsDeleted);
    }


    private static void ConfigureReminders(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<DbReminder>();
        entity.ToTable("Reminders");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Title).HasMaxLength(160).IsRequired();
        entity.Property(x => x.Kind).HasMaxLength(24).IsRequired();
        entity.Property(x => x.DueDate).HasColumnType("TEXT");
        entity.Property(x => x.Note).HasMaxLength(500);
        entity.HasIndex(x => new { x.IsCompleted, x.DueDate });
        entity.HasIndex(x => x.NotifyAtUtc);
        entity.HasQueryFilter(x => !x.IsDeleted);
    }

    private static void ConfigureRecurringTransactions(ModelBuilder modelBuilder)
    {
        var schedule = modelBuilder.Entity<DbRecurringTransaction>();
        schedule.ToTable("RecurringTransactions", table =>
        {
            table.HasCheckConstraint("CK_RecurringTransactions_Amount_Positive", "AmountMinor > 0");
            table.HasCheckConstraint("CK_RecurringTransactions_Interval_Positive", "IntervalCount > 0");
            table.HasCheckConstraint("CK_RecurringTransactions_CurrencyCode_Length", "length(CurrencyCode) = 3");
        });
        schedule.HasKey(x => x.Id);
        schedule.Property(x => x.Title).HasMaxLength(160).IsRequired();
        schedule.Property(x => x.Kind).HasMaxLength(20).IsRequired();
        schedule.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
        schedule.Property(x => x.StartDate).HasColumnType("TEXT");
        schedule.Property(x => x.NextOccurrenceDate).HasColumnType("TEXT");
        schedule.Property(x => x.EndDate).HasColumnType("TEXT");
        schedule.Property(x => x.IntervalUnit).HasMaxLength(12).IsRequired();
        schedule.HasIndex(x => new { x.IsPaused, x.NextOccurrenceDate });
        schedule.HasOne<DbAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        schedule.HasOne<DbAccount>().WithMany().HasForeignKey(x => x.CategoryAccountId).OnDelete(DeleteBehavior.Restrict);
        schedule.HasQueryFilter(x => !x.IsDeleted);

        var occurrence = modelBuilder.Entity<DbRecurringTransactionOccurrence>();
        occurrence.ToTable("RecurringTransactionOccurrences");
        occurrence.HasKey(x => x.Id);
        occurrence.Property(x => x.OccurrenceDate).HasColumnType("TEXT");
        occurrence.Property(x => x.Status).HasMaxLength(20).IsRequired();
        occurrence.HasIndex(x => new { x.RecurringTransactionId, x.OccurrenceDate }).IsUnique()
            .HasDatabaseName("IX_RecurringTransactionOccurrences_RecurringTransactionId_OccurrenceDate");
        occurrence.HasIndex(x => x.TransactionId).IsUnique().HasFilter("\"TransactionId\" IS NOT NULL");
        occurrence.HasOne<DbRecurringTransaction>().WithMany()
            .HasForeignKey(x => x.RecurringTransactionId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_RecurringTransactionOccurrences_RecurringTransactions_RecurringTransactionId");
        occurrence.HasOne<DbLedgerTransaction>().WithMany()
            .HasForeignKey(x => x.TransactionId).OnDelete(DeleteBehavior.Restrict);
        occurrence.HasQueryFilter(x => !x.IsDeleted);
    }


    private static void ConfigurePlanningAndFiscalFeatures(ModelBuilder modelBuilder)
    {
        var budget = modelBuilder.Entity<DbBudget>();
        budget.ToTable("Budgets", table =>
        {
            table.HasCheckConstraint("CK_Budgets_Limit_Positive", "LimitMinor > 0");
            table.HasCheckConstraint("CK_Budgets_Period_Valid", "PeriodEnd >= PeriodStart");
            table.HasCheckConstraint("CK_Budgets_Currency_Length", "length(CurrencyCode) = 3");
        });
        budget.HasKey(x => x.Id);
        budget.Property(x => x.PeriodStart).HasColumnType("TEXT");
        budget.Property(x => x.PeriodEnd).HasColumnType("TEXT");
        budget.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
        budget.Property(x => x.Note).HasMaxLength(300);
        budget.HasIndex(x => new { x.CategoryAccountId, x.PeriodStart, x.PeriodEnd }).IsUnique();
        budget.HasOne(x => x.CategoryAccount).WithMany().HasForeignKey(x => x.CategoryAccountId).OnDelete(DeleteBehavior.Restrict);
        budget.HasQueryFilter(x => !x.IsDeleted);

        var tag = modelBuilder.Entity<DbTag>();
        tag.ToTable("Tags");
        tag.HasKey(x => x.Id);
        tag.Property(x => x.Name).HasMaxLength(40).IsRequired();
        tag.Property(x => x.Color).HasMaxLength(16);
        tag.HasIndex(x => x.Name).IsUnique().HasFilter("\"IsDeleted\" = 0");
        tag.HasQueryFilter(x => !x.IsDeleted);

        var transactionTag = modelBuilder.Entity<DbTransactionTag>();
        transactionTag.ToTable("TransactionTags");
        transactionTag.HasKey(x => x.Id);
        transactionTag.HasIndex(x => new { x.TransactionId, x.TagId }).IsUnique();
        transactionTag.HasOne<DbLedgerTransaction>().WithMany().HasForeignKey(x => x.TransactionId).OnDelete(DeleteBehavior.Cascade);
        transactionTag.HasOne<DbTag>().WithMany().HasForeignKey(x => x.TagId).OnDelete(DeleteBehavior.Restrict);
        transactionTag.HasQueryFilter(x => !x.IsDeleted);

        var period = modelBuilder.Entity<DbFiscalPeriod>();
        period.ToTable("FiscalPeriods", table =>
            table.HasCheckConstraint("CK_FiscalPeriods_DateRange", "EndDate >= StartDate"));
        period.HasKey(x => x.Id);
        period.Property(x => x.Name).HasMaxLength(80).IsRequired();
        period.Property(x => x.StartDate).HasColumnType("TEXT");
        period.Property(x => x.EndDate).HasColumnType("TEXT");
        period.HasIndex(x => x.StartDate).IsUnique();
        period.HasIndex(x => new { x.StartDate, x.EndDate });
        period.HasQueryFilter(x => !x.IsDeleted);

        var sequence = modelBuilder.Entity<DbDocumentSequence>();
        sequence.ToTable("DocumentSequences", table =>
            table.HasCheckConstraint("CK_DocumentSequences_LastNumber_NonNegative", "LastNumber >= 0"));
        sequence.HasKey(x => x.Id);
        sequence.HasIndex(x => x.FiscalPeriodId).IsUnique();
        sequence.HasOne<DbFiscalPeriod>().WithMany().HasForeignKey(x => x.FiscalPeriodId).OnDelete(DeleteBehavior.Cascade);
        sequence.HasQueryFilter(x => !x.IsDeleted);
    }



    private static void ConfigurePhysicalAssets(ModelBuilder modelBuilder)
    {
        var asset = modelBuilder.Entity<DbPhysicalAsset>();
        asset.ToTable("PhysicalAssets", table =>
        {
            table.HasCheckConstraint("CK_PhysicalAssets_Quantity_Positive", "Quantity > 0");
            table.HasCheckConstraint("CK_PhysicalAssets_Cost_NonNegative", "AcquisitionCostMinor >= 0");
            table.HasCheckConstraint("CK_PhysicalAssets_Currency_Length", "length(CurrencyCode) = 3");
        });
        asset.HasKey(x => x.Id);
        asset.Property(x => x.Name).HasMaxLength(120).IsRequired();
        asset.Property(x => x.Type);
        asset.Property(x => x.Quantity).HasColumnType("TEXT");
        asset.Property(x => x.Unit).HasMaxLength(24);
        asset.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
        asset.Property(x => x.AcquiredOn).HasColumnType("TEXT");
        asset.Property(x => x.Note).HasMaxLength(500);
        asset.HasIndex(x => new { x.IsArchived, x.Name });
        asset.HasQueryFilter(x => !x.IsDeleted);

        var valuation = modelBuilder.Entity<DbAssetValuation>();
        valuation.ToTable("AssetValuations", table =>
        {
            table.HasCheckConstraint("CK_AssetValuations_Value_NonNegative", "ValueMinor >= 0");
            table.HasCheckConstraint("CK_AssetValuations_Currency_Length", "length(CurrencyCode) = 3");
        });
        valuation.HasKey(x => x.Id);
        valuation.Property(x => x.ValuationDate).HasColumnType("TEXT");
        valuation.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
        valuation.Property(x => x.Note).HasMaxLength(300);
        valuation.HasIndex(x => new { x.AssetId, x.ValuationDate });
        valuation.HasOne<DbPhysicalAsset>().WithMany(x => x.Valuations)
            .HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
        valuation.HasQueryFilter(x => !x.IsDeleted);
    }


    private static void ConfigureDocumentAttachments(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<DbDocumentAttachment>();
        entity.ToTable("DocumentAttachments", table =>
        {
            table.HasCheckConstraint("CK_DocumentAttachments_ExactlyOneOwner",
                "(TransactionId IS NOT NULL AND PhysicalAssetId IS NULL) OR (TransactionId IS NULL AND PhysicalAssetId IS NOT NULL)");
            table.HasCheckConstraint("CK_DocumentAttachments_Length", "Length > 0 AND Length <= 15728640");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.OriginalFileName).HasMaxLength(180).IsRequired();
        entity.Property(x => x.StoredFileName).HasMaxLength(48).IsRequired();
        entity.Property(x => x.ContentType).HasMaxLength(40).IsRequired();
        entity.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
        entity.HasIndex(x => x.TransactionId);
        entity.HasIndex(x => x.PhysicalAssetId);
        entity.HasIndex(x => x.StoredFileName).IsUnique();
        entity.HasOne<DbLedgerTransaction>().WithMany().HasForeignKey(x => x.TransactionId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<DbPhysicalAsset>().WithMany().HasForeignKey(x => x.PhysicalAssetId).OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => !x.IsDeleted);
    }

    private static void ConfigureCategorySuggestionRules(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<DbCategorySuggestionRule>();
        entity.ToTable("CategorySuggestionRules", table =>
        {
            table.HasCheckConstraint("CK_CategorySuggestionRules_Kind", "TransactionKind IN ('Expense', 'Income')");
            table.HasCheckConstraint("CK_CategorySuggestionRules_MatchText_Length", "length(MatchText) BETWEEN 3 AND 120");
            table.HasCheckConstraint("CK_CategorySuggestionRules_Normalized_Length", "length(NormalizedMatchText) BETWEEN 3 AND 120");
            table.HasCheckConstraint("CK_CategorySuggestionRules_UsageCount", "UsageCount >= 0");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.MatchText).HasMaxLength(120).IsRequired();
        entity.Property(x => x.NormalizedMatchText).HasMaxLength(120).IsRequired();
        entity.Property(x => x.TransactionKind).HasConversion<string>().HasMaxLength(12);
        entity.Property(x => x.LastUsedAtUtc).HasColumnType("TEXT");
        entity.HasIndex(x => new { x.TransactionKind, x.IsEnabled, x.NormalizedMatchText });
        entity.HasIndex(x => x.CategoryId);
        entity.HasIndex(x => x.AccountId);
        entity.HasOne<DbAccount>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<DbAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => !x.IsDeleted);
    }

    private void PrepareSyncOutbox()
    {
        var changes = GetPendingTransactionChanges();
        if (changes.Count == 0)
            return;

        var nextSequence = SyncOutbox.Select(x => (long?)x.DeviceSequence).Max() ?? 0L;
        foreach (var change in changes)
            SyncOutbox.Add(CreateOutboxItem(change.Entity, change.Operation, checked(++nextSequence)));
    }

    private async Task PrepareSyncOutboxAsync(CancellationToken cancellationToken)
    {
        var changes = GetPendingTransactionChanges();
        if (changes.Count == 0)
            return;

        var nextSequence = await SyncOutbox.Select(x => (long?)x.DeviceSequence)
            .MaxAsync(cancellationToken) ?? 0L;
        foreach (var change in changes)
            SyncOutbox.Add(CreateOutboxItem(change.Entity, change.Operation, checked(++nextSequence)));
    }

    private List<(DbLedgerTransaction Entity, SyncOperation Operation)> GetPendingTransactionChanges() =>
        ChangeTracker.Entries<DbLedgerTransaction>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .OrderBy(entry => entry.Entity.Id)
            .Select(entry => (
                entry.Entity,
                entry.State == EntityState.Deleted || entry.Entity.IsDeleted
                    ? SyncOperation.Delete
                    : SyncOperation.Upsert))
            .ToList();

    private DbSyncOutboxItem CreateOutboxItem(
        DbLedgerTransaction transaction,
        SyncOperation operation,
        long deviceSequence)
    {
        IReadOnlyList<SyncFieldValue> fields = operation == SyncOperation.Delete
            ? []
            : CreateTransactionFields(transaction);

        return new DbSyncOutboxItem
        {
            Id = Guid.CreateVersion7(),
            AggregateId = transaction.Id,
            AggregateType = "ledger-transaction",
            Operation = (int)operation,
            SchemaVersion = 1,
            ChangedAtUtc = _timeProvider.GetUtcNow(),
            DeviceSequence = deviceSequence,
            FieldsJson = JsonSerializer.Serialize(fields)
        };
    }

    private IReadOnlyList<SyncFieldValue> CreateTransactionFields(DbLedgerTransaction transaction)
    {
        var postings = transaction.Postings.Count > 0
            ? transaction.Postings.ToArray()
            : Postings.AsNoTracking().Where(x => x.TransactionId == transaction.Id).ToArray();

        return
        [
            SyncFieldValue.From("businessDate", transaction.BusinessDate),
            SyncFieldValue.From("kind", transaction.Kind.ToString()),
            SyncFieldValue.From("description", transaction.Description),
            SyncFieldValue.From("source", transaction.Source.ToString()),
            SyncFieldValue.From("isConfirmed", transaction.IsConfirmed),
            SyncFieldValue.From("partyId", transaction.PartyId),
            SyncFieldValue.From("loanId", transaction.LoanId),
            SyncFieldValue.From("dueDate", transaction.DueDate),
            SyncFieldValue.From("counterAmountMinor", transaction.CounterAmountMinor),
            SyncFieldValue.From("postings", postings.Select(posting => new
            {
                posting.Id,
                posting.AccountId,
                posting.AmountMinor,
                posting.CurrencyCode,
                posting.PartyId,
                posting.Memo
            }).ToArray())
        ];
    }

    private static void ConfigureSyncOutbox(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<DbSyncOutboxItem>();
        entity.ToTable("SyncOutbox");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.AggregateType).HasMaxLength(64).IsRequired();
        entity.Property(x => x.FieldsJson).IsRequired();
        entity.Property(x => x.ChangedAtUtc).HasColumnType("TEXT");
        entity.Property(x => x.AcknowledgedAtUtc).HasColumnType("TEXT");
        entity.Property(x => x.CreatedAtUtc).HasColumnType("TEXT");
        entity.Property(x => x.UpdatedAtUtc).HasColumnType("TEXT");
        entity.HasIndex(x => x.DeviceSequence).IsUnique();
        entity.HasIndex(x => new { x.AcknowledgedAtUtc, x.DeviceSequence });
        entity.HasIndex(x => new { x.AggregateType, x.AggregateId, x.DeviceSequence });
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
