using Hesabyar.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hesabyar.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("202611150003_Cheques")]
public sealed class Cheques : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Cheques",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Direction = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                BankName = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                SerialNumber = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                SayadNumber = table.Column<string>(type: "TEXT", maxLength: 24, nullable: true),
                DueDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                PersonId = table.Column<Guid>(type: "TEXT", nullable: true),
                AmountMinor = table.Column<long>(type: "INTEGER", nullable: false),
                Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                Status = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                IsArchived = table.Column<bool>(type: "INTEGER", nullable: false),
                SettlementAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                SettlementTransactionId = table.Column<Guid>(type: "TEXT", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Cheques", x => x.Id);
                table.CheckConstraint("CK_Cheques_Amount_Positive", "AmountMinor > 0");
            });

        migrationBuilder.CreateIndex("IX_Cheques_Direction_BankName_SerialNumber", "Cheques",
            new[] { "Direction", "BankName", "SerialNumber" }, unique: true, filter: "\"IsDeleted\" = 0");
        migrationBuilder.CreateIndex("IX_Cheques_SayadNumber", "Cheques", "SayadNumber", unique: true,
            filter: "\"SayadNumber\" IS NOT NULL AND \"IsDeleted\" = 0");
        migrationBuilder.CreateIndex("IX_Cheques_IsArchived_DueDate", "Cheques", new[] { "IsArchived", "DueDate" });
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("Cheques");

    protected override void BuildTargetModel(ModelBuilder modelBuilder) =>
        new AppDbContextModelSnapshot().BuildTargetModelForMigration(modelBuilder);
}
