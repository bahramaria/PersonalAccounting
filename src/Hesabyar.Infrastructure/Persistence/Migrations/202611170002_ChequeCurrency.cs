using Hesabyar.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hesabyar.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("202611170002_ChequeCurrency")]
public sealed class ChequeCurrency : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "CurrencyCode",
            table: "Cheques",
            type: "TEXT",
            maxLength: 3,
            nullable: false,
            defaultValue: "IRR");

        migrationBuilder.Sql("""
            CREATE TRIGGER "TR_Cheques_CurrencyCode_Insert"
            BEFORE INSERT ON "Cheques"
            WHEN length(NEW."CurrencyCode") != 3
            BEGIN
                SELECT RAISE(ABORT, 'Invalid cheque currency code');
            END;
            """);
        migrationBuilder.Sql("""
            CREATE TRIGGER "TR_Cheques_CurrencyCode_Update"
            BEFORE UPDATE OF "CurrencyCode" ON "Cheques"
            WHEN length(NEW."CurrencyCode") != 3
            BEGIN
                SELECT RAISE(ABORT, 'Invalid cheque currency code');
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "TR_Cheques_CurrencyCode_Insert";""");
        migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "TR_Cheques_CurrencyCode_Update";""");
        migrationBuilder.DropColumn(name: "CurrencyCode", table: "Cheques");
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder) =>
        new AppDbContextModelSnapshot().BuildTargetModelForMigration(modelBuilder);
}
