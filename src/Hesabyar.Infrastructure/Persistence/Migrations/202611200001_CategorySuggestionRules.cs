using Hesabyar.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hesabyar.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("202611200001_CategorySuggestionRules")]
public sealed class CategorySuggestionRules : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "CategorySuggestionRules",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                MatchText = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                NormalizedMatchText = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                TransactionKind = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                CategoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                AccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                UsageCount = table.Column<int>(type: "INTEGER", nullable: false),
                LastUsedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CategorySuggestionRules", x => x.Id);
                table.CheckConstraint("CK_CategorySuggestionRules_Kind", "TransactionKind IN ('Expense', 'Income')");
                table.CheckConstraint("CK_CategorySuggestionRules_MatchText_Length", "length(MatchText) BETWEEN 3 AND 120");
                table.CheckConstraint("CK_CategorySuggestionRules_Normalized_Length", "length(NormalizedMatchText) BETWEEN 3 AND 120");
                table.CheckConstraint("CK_CategorySuggestionRules_UsageCount", "UsageCount >= 0");
                table.ForeignKey("FK_CategorySuggestionRules_Accounts_CategoryId", x => x.CategoryId, "Accounts", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_CategorySuggestionRules_Accounts_AccountId", x => x.AccountId, "Accounts", "Id", onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex(
            name: "IX_CategorySuggestionRules_TransactionKind_IsEnabled_NormalizedMatchText",
            table: "CategorySuggestionRules",
            columns: new[] { "TransactionKind", "IsEnabled", "NormalizedMatchText" });
        migrationBuilder.CreateIndex(
            name: "IX_CategorySuggestionRules_CategoryId",
            table: "CategorySuggestionRules",
            column: "CategoryId");
        migrationBuilder.CreateIndex(
            name: "IX_CategorySuggestionRules_AccountId",
            table: "CategorySuggestionRules",
            column: "AccountId");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable("CategorySuggestionRules");

    protected override void BuildTargetModel(ModelBuilder modelBuilder) =>
        new AppDbContextModelSnapshot().BuildTargetModelForMigration(modelBuilder);
}
