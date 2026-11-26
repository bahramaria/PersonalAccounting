using Hesabyar.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hesabyar.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("202611160001_RemindersAndRecurringTransactions")]
public sealed class RemindersAndRecurringTransactions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Reminders",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Title = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                Kind = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                DueDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                NotifyAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                IsCompleted = table.Column<bool>(type: "INTEGER", nullable: false),
                CompletedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                RelatedEntityId = table.Column<Guid>(type: "TEXT", nullable: true),
                Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Reminders", x => x.Id));

        migrationBuilder.CreateTable(
            name: "RecurringTransactions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Title = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                CurrencyCode = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                AmountMinor = table.Column<long>(type: "INTEGER", nullable: false),
                AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                CategoryAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                StartDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                NextOccurrenceDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                EndDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                IntervalCount = table.Column<int>(type: "INTEGER", nullable: false),
                IntervalUnit = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                IsPaused = table.Column<bool>(type: "INTEGER", nullable: false),
                LastGeneratedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RecurringTransactions", x => x.Id);
                table.CheckConstraint("CK_RecurringTransactions_Amount_Positive", "AmountMinor > 0");
                table.CheckConstraint("CK_RecurringTransactions_Interval_Positive", "IntervalCount > 0");
                table.CheckConstraint("CK_RecurringTransactions_CurrencyCode_Length", "length(CurrencyCode) = 3");
                table.ForeignKey("FK_RecurringTransactions_Accounts_AccountId", x => x.AccountId, "Accounts", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_RecurringTransactions_Accounts_CategoryAccountId", x => x.CategoryAccountId, "Accounts", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "RecurringTransactionOccurrences",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                RecurringTransactionId = table.Column<Guid>(type: "TEXT", nullable: false),
                OccurrenceDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                TransactionId = table.Column<Guid>(type: "TEXT", nullable: true),
                Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                GeneratedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RecurringTransactionOccurrences", x => x.Id);
                table.ForeignKey("FK_RecurringTransactionOccurrences_RecurringTransactions_RecurringTransactionId",
                    x => x.RecurringTransactionId, "RecurringTransactions", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_RecurringTransactionOccurrences_Transactions_TransactionId",
                    x => x.TransactionId, "Transactions", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_Reminders_IsCompleted_DueDate", "Reminders", new[] { "IsCompleted", "DueDate" });
        migrationBuilder.CreateIndex("IX_Reminders_NotifyAtUtc", "Reminders", "NotifyAtUtc");
        migrationBuilder.CreateIndex("IX_RecurringTransactions_AccountId", "RecurringTransactions", "AccountId");
        migrationBuilder.CreateIndex("IX_RecurringTransactions_CategoryAccountId", "RecurringTransactions", "CategoryAccountId");
        migrationBuilder.CreateIndex("IX_RecurringTransactions_IsPaused_NextOccurrenceDate", "RecurringTransactions", new[] { "IsPaused", "NextOccurrenceDate" });
        migrationBuilder.CreateIndex("IX_RecurringTransactionOccurrences_RecurringTransactionId_OccurrenceDate",
            "RecurringTransactionOccurrences", new[] { "RecurringTransactionId", "OccurrenceDate" }, unique: true);
        migrationBuilder.CreateIndex("IX_RecurringTransactionOccurrences_TransactionId",
            "RecurringTransactionOccurrences", "TransactionId", unique: true, filter: "\"TransactionId\" IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("RecurringTransactionOccurrences");
        migrationBuilder.DropTable("RecurringTransactions");
        migrationBuilder.DropTable("Reminders");
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder) =>
        new AppDbContextModelSnapshot().BuildTargetModelForMigration(modelBuilder);
}
