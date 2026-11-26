using Hesabyar.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hesabyar.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("202611150005_BankSmsInbox")]
public sealed class BankSmsInbox : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SmsInboxItems",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Sender = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                Body = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                DedupeHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                BankKey = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                ParseStatus = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                TransactionType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                AmountMinor = table.Column<long>(type: "INTEGER", nullable: true),
                AmountUnitKnown = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                BalanceMinor = table.Column<long>(type: "INTEGER", nullable: true),
                CardLastFour = table.Column<string>(type: "TEXT", maxLength: 4, nullable: true),
                DateText = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                TransactionTime = table.Column<string>(type: "TEXT", maxLength: 5, nullable: true),
                Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                TransactionId = table.Column<Guid>(type: "TEXT", nullable: true),
                BalanceAdjustmentTransactionId = table.Column<Guid>(type: "TEXT", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_SmsInboxItems", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_SmsInboxItems_DedupeHash",
            table: "SmsInboxItems",
            column: "DedupeHash",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_SmsInboxItems_Status_CreatedAtUtc",
            table: "SmsInboxItems",
            columns: new[] { "Status", "CreatedAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "SmsInboxItems");
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder) =>
        new AppDbContextModelSnapshot().BuildTargetModelForMigration(modelBuilder);
}
