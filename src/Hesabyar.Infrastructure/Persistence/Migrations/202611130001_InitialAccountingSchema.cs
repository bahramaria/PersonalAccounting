using Hesabyar.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hesabyar.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("202611130001_InitialAccountingSchema")]
public sealed class InitialAccountingSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Accounts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                Type = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                Kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                ParentId = table.Column<Guid>(type: "TEXT", nullable: true),
                CurrencyCode = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                SystemKey = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                Icon = table.Column<string>(type: "TEXT", nullable: true),
                Color = table.Column<string>(type: "TEXT", nullable: true),
                SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                IsArchived = table.Column<bool>(type: "INTEGER", nullable: false),
                IncludeInTotal = table.Column<bool>(type: "INTEGER", nullable: false),
                BankName = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                CardLast4 = table.Column<string>(type: "TEXT", maxLength: 4, nullable: true),
                AccountNumberMasked = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Accounts", x => x.Id);
                table.CheckConstraint("CK_Accounts_CurrencyCode_Length", "length(CurrencyCode) = 3");
                table.ForeignKey("FK_Accounts_Accounts_ParentId", x => x.ParentId, "Accounts", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Transactions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                BusinessDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                PartyId = table.Column<Guid>(type: "TEXT", nullable: true),
                Source = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                SmsMessageId = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true),
                IsConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Transactions", x => x.Id));

        migrationBuilder.CreateTable(
            name: "Postings",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                TransactionId = table.Column<Guid>(type: "TEXT", nullable: false),
                AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                AmountMinor = table.Column<long>(type: "INTEGER", nullable: false),
                CurrencyCode = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                PartyId = table.Column<Guid>(type: "TEXT", nullable: true),
                Memo = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Postings", x => x.Id);
                table.CheckConstraint("CK_Postings_Amount_NonZero", "AmountMinor <> 0");
                table.CheckConstraint("CK_Postings_CurrencyCode_Length", "length(CurrencyCode) = 3");
                table.ForeignKey("FK_Postings_Accounts_AccountId", x => x.AccountId, "Accounts", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_Postings_Transactions_TransactionId", x => x.TransactionId, "Transactions", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("IX_Accounts_ParentId", "Accounts", "ParentId");
        migrationBuilder.CreateIndex("IX_Accounts_SystemKey_CurrencyCode", "Accounts", new[] { "SystemKey", "CurrencyCode" }, unique: true, filter: "\"SystemKey\" IS NOT NULL");
        migrationBuilder.CreateIndex("IX_Postings_AccountId_TransactionId", "Postings", new[] { "AccountId", "TransactionId" });
        migrationBuilder.CreateIndex("IX_Postings_PartyId", "Postings", "PartyId");
        migrationBuilder.CreateIndex("IX_Postings_TransactionId", "Postings", "TransactionId");
        migrationBuilder.CreateIndex("IX_Transactions_BusinessDate", "Transactions", "BusinessDate", descending: new[] { true });
        migrationBuilder.CreateIndex("IX_Transactions_PartyId", "Transactions", "PartyId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("Postings");
        migrationBuilder.DropTable("Transactions");
        migrationBuilder.DropTable("Accounts");
    }
}
