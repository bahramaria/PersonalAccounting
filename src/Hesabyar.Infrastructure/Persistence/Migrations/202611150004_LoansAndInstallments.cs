using Hesabyar.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hesabyar.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("202611150004_LoansAndInstallments")]
public sealed class LoansAndInstallments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Loans",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                Direction = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                ScheduleType = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                PrincipalMinor = table.Column<long>(type: "INTEGER", nullable: false),
                AnnualRateBasisPoints = table.Column<int>(type: "INTEGER", nullable: false),
                StartDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                LoanAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                PersonId = table.Column<Guid>(type: "TEXT", nullable: true),
                Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                IsClosed = table.Column<bool>(type: "INTEGER", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Loans", x => x.Id);
                table.CheckConstraint("CK_Loans_Principal_Positive", "PrincipalMinor > 0");
                table.CheckConstraint("CK_Loans_AnnualRate_NonNegative", "AnnualRateBasisPoints >= 0");
                table.ForeignKey("FK_Loans_Accounts_AccountId", x => x.AccountId, "Accounts", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_Loans_Accounts_LoanAccountId", x => x.LoanAccountId, "Accounts", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_Loans_Parties_PersonId", x => x.PersonId, "Parties", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "LoanInstallments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                LoanId = table.Column<Guid>(type: "TEXT", nullable: false),
                Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                DueDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                PrincipalDueMinor = table.Column<long>(type: "INTEGER", nullable: false),
                InterestDueMinor = table.Column<long>(type: "INTEGER", nullable: false),
                PaidPrincipalMinor = table.Column<long>(type: "INTEGER", nullable: false),
                PaidInterestMinor = table.Column<long>(type: "INTEGER", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LoanInstallments", x => x.Id);
                table.CheckConstraint("CK_LoanInstallments_Principal_NonNegative", "PrincipalDueMinor > 0");
                table.CheckConstraint("CK_LoanInstallments_Interest_NonNegative", "InterestDueMinor >= 0");
                table.CheckConstraint("CK_LoanInstallments_PaidPrincipal_NonNegative", "PaidPrincipalMinor >= 0 AND PaidPrincipalMinor <= PrincipalDueMinor");
                table.CheckConstraint("CK_LoanInstallments_PaidInterest_NonNegative", "PaidInterestMinor >= 0 AND PaidInterestMinor <= InterestDueMinor");
                table.ForeignKey("FK_LoanInstallments_Loans_LoanId", x => x.LoanId, "Loans", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.AddColumn<Guid>(name: "LoanId", table: "Transactions", type: "TEXT", nullable: true);
        migrationBuilder.CreateIndex("IX_Loans_AccountId", "Loans", "AccountId");
        migrationBuilder.CreateIndex("IX_Loans_LoanAccountId", "Loans", "LoanAccountId");
        migrationBuilder.CreateIndex("IX_Loans_PersonId", "Loans", "PersonId");
        migrationBuilder.CreateIndex("IX_Loans_StartDate", "Loans", "StartDate");
        migrationBuilder.CreateIndex("IX_LoanInstallments_LoanId_DueDate_Sequence", "LoanInstallments",
            new[] { "LoanId", "DueDate", "Sequence" }, unique: true);
        migrationBuilder.CreateIndex("IX_Transactions_LoanId", "Transactions", "LoanId");
        migrationBuilder.Sql("""
            CREATE TRIGGER "TR_Transactions_LoanId_Insert"
            BEFORE INSERT ON "Transactions"
            WHEN NEW."LoanId" IS NOT NULL
                AND NOT EXISTS (SELECT 1 FROM "Loans" WHERE "Id" = NEW."LoanId")
            BEGIN
                SELECT RAISE(ABORT, 'FOREIGN KEY constraint failed');
            END;
            """);
        migrationBuilder.Sql("""
            CREATE TRIGGER "TR_Transactions_LoanId_Update"
            BEFORE UPDATE OF "LoanId" ON "Transactions"
            WHEN NEW."LoanId" IS NOT NULL
                AND NOT EXISTS (SELECT 1 FROM "Loans" WHERE "Id" = NEW."LoanId")
            BEGIN
                SELECT RAISE(ABORT, 'FOREIGN KEY constraint failed');
            END;
            """);
        migrationBuilder.Sql("""
            CREATE TRIGGER "TR_Loans_ReferencedDelete"
            BEFORE DELETE ON "Loans"
            WHEN EXISTS (SELECT 1 FROM "Transactions" WHERE "LoanId" = OLD."Id")
            BEGIN
                SELECT RAISE(ABORT, 'FOREIGN KEY constraint failed');
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_Transactions_LoanId", table: "Transactions");
        migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "TR_Transactions_LoanId_Insert";""");
        migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "TR_Transactions_LoanId_Update";""");
        migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "TR_Loans_ReferencedDelete";""");
        migrationBuilder.DropColumn(name: "LoanId", table: "Transactions");
        migrationBuilder.DropTable(name: "LoanInstallments");
        migrationBuilder.DropTable(name: "Loans");
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder) =>
        new AppDbContextModelSnapshot().BuildTargetModelForMigration(modelBuilder);
}
