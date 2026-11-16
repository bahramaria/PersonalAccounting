using Hesabyar.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hesabyar.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("202611170001_PlanningAndFiscalFeatures")]
public sealed class PlanningAndFiscalFeatures : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(name: "FiscalPeriodId", table: "Transactions", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<string>(name: "DocumentNumber", table: "Transactions", type: "TEXT", maxLength: 32, nullable: true);

        migrationBuilder.CreateTable("FiscalPeriods", table => new
        {
            Id = table.Column<Guid>(type: "TEXT", nullable: false),
            Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
            StartDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
            EndDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
            IsClosed = table.Column<bool>(type: "INTEGER", nullable: false),
            CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
            UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
            IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_FiscalPeriods", x => x.Id);
            table.CheckConstraint("CK_FiscalPeriods_DateRange", "EndDate >= StartDate");
        });

        migrationBuilder.CreateTable("Tags", table => new
        {
            Id = table.Column<Guid>(type: "TEXT", nullable: false),
            Name = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
            Color = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
            CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
            UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
            IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
        }, constraints: table => table.PrimaryKey("PK_Tags", x => x.Id));

        migrationBuilder.CreateTable("Budgets", table => new
        {
            Id = table.Column<Guid>(type: "TEXT", nullable: false),
            CategoryAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
            PeriodStart = table.Column<DateOnly>(type: "TEXT", nullable: false),
            PeriodEnd = table.Column<DateOnly>(type: "TEXT", nullable: false),
            LimitMinor = table.Column<long>(type: "INTEGER", nullable: false),
            CurrencyCode = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
            IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
            Note = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
            CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
            UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
            IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_Budgets", x => x.Id);
            table.CheckConstraint("CK_Budgets_Limit_Positive", "LimitMinor > 0");
            table.CheckConstraint("CK_Budgets_Period_Valid", "PeriodEnd >= PeriodStart");
            table.CheckConstraint("CK_Budgets_Currency_Length", "length(CurrencyCode) = 3");
            table.ForeignKey("FK_Budgets_Accounts_CategoryAccountId", x => x.CategoryAccountId, "Accounts", "Id", onDelete: ReferentialAction.Restrict);
        });

        migrationBuilder.CreateTable("DocumentSequences", table => new
        {
            Id = table.Column<Guid>(type: "TEXT", nullable: false),
            FiscalPeriodId = table.Column<Guid>(type: "TEXT", nullable: false),
            LastNumber = table.Column<int>(type: "INTEGER", nullable: false),
            CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
            UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
            IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_DocumentSequences", x => x.Id);
            table.CheckConstraint("CK_DocumentSequences_LastNumber_NonNegative", "LastNumber >= 0");
            table.ForeignKey("FK_DocumentSequences_FiscalPeriods_FiscalPeriodId", x => x.FiscalPeriodId, "FiscalPeriods", "Id", onDelete: ReferentialAction.Cascade);
        });

        migrationBuilder.CreateTable("TransactionTags", table => new
        {
            Id = table.Column<Guid>(type: "TEXT", nullable: false),
            TransactionId = table.Column<Guid>(type: "TEXT", nullable: false),
            TagId = table.Column<Guid>(type: "TEXT", nullable: false),
            CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
            UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
            IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_TransactionTags", x => x.Id);
            table.ForeignKey("FK_TransactionTags_Tags_TagId", x => x.TagId, "Tags", "Id", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_TransactionTags_Transactions_TransactionId", x => x.TransactionId, "Transactions", "Id", onDelete: ReferentialAction.Cascade);
        });

        migrationBuilder.CreateIndex("IX_Transactions_FiscalPeriodId_DocumentNumber", "Transactions",
            new[] { "FiscalPeriodId", "DocumentNumber" }, unique: true,
            filter: "\"FiscalPeriodId\" IS NOT NULL AND \"DocumentNumber\" IS NOT NULL");
        migrationBuilder.CreateIndex("IX_Budgets_CategoryAccountId_PeriodStart_PeriodEnd", "Budgets",
            new[] { "CategoryAccountId", "PeriodStart", "PeriodEnd" }, unique: true);
        migrationBuilder.CreateIndex("IX_Tags_Name", "Tags", "Name", unique: true, filter: "\"IsDeleted\" = 0");
        migrationBuilder.CreateIndex("IX_TransactionTags_TransactionId_TagId", "TransactionTags", new[] { "TransactionId", "TagId" }, unique: true);
        migrationBuilder.CreateIndex("IX_FiscalPeriods_StartDate", "FiscalPeriods", "StartDate", unique: true);
        migrationBuilder.CreateIndex("IX_FiscalPeriods_StartDate_EndDate", "FiscalPeriods", new[] { "StartDate", "EndDate" });
        migrationBuilder.CreateIndex("IX_DocumentSequences_FiscalPeriodId", "DocumentSequences", "FiscalPeriodId", unique: true);
        migrationBuilder.CreateIndex("IX_TransactionTags_TagId", "TransactionTags", "TagId");
        migrationBuilder.CreateIndex("IX_Budgets_CategoryAccountId", "Budgets", "CategoryAccountId");
        migrationBuilder.CreateIndex("IX_DocumentSequences_FiscalPeriodId", "DocumentSequences", "FiscalPeriodId", unique: true);
        migrationBuilder.CreateIndex("IX_Transactions_FiscalPeriodId", "Transactions", "FiscalPeriodId");
        migrationBuilder.AddForeignKey("FK_Transactions_FiscalPeriods_FiscalPeriodId", "Transactions", "FiscalPeriodId", "FiscalPeriods", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey("FK_Transactions_FiscalPeriods_FiscalPeriodId", "Transactions");
        migrationBuilder.DropIndex("IX_Transactions_FiscalPeriodId_DocumentNumber", "Transactions");
        migrationBuilder.DropIndex("IX_Transactions_FiscalPeriodId", "Transactions");
        migrationBuilder.DropColumn("FiscalPeriodId", "Transactions");
        migrationBuilder.DropColumn("DocumentNumber", "Transactions");
        migrationBuilder.DropTable("TransactionTags");
        migrationBuilder.DropTable("Budgets");
        migrationBuilder.DropTable("DocumentSequences");
        migrationBuilder.DropTable("Tags");
        migrationBuilder.DropTable("FiscalPeriods");
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder) =>
        new AppDbContextModelSnapshot().BuildTargetModelForMigration(modelBuilder);
}
