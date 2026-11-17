using Hesabyar.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hesabyar.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("202611170002_CurrencyExchange")]
public sealed class CurrencyExchange : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_Transactions_DocumentNumber",
            table: "Transactions",
            column: "DocumentNumber",
            unique: true,
            filter: "\"DocumentNumber\" IS NOT NULL");

        migrationBuilder.AddColumn<long>(
            name: "CounterAmountMinor",
            table: "Transactions",
            type: "INTEGER",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_Transactions_DocumentNumber", table: "Transactions");
        migrationBuilder.DropColumn(name: "CounterAmountMinor", table: "Transactions");
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder) =>
        new AppDbContextModelSnapshot().BuildTargetModelForMigration(modelBuilder);
}
