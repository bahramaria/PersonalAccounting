using Hesabyar.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hesabyar.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("202611190001_PhysicalAssets")]
public sealed class PhysicalAssets : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PhysicalAssets",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                Type = table.Column<int>(type: "INTEGER", nullable: false),
                Quantity = table.Column<decimal>(type: "TEXT", nullable: false),
                Unit = table.Column<string>(type: "TEXT", maxLength: 24, nullable: true),
                CurrencyCode = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                AcquiredOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                AcquisitionCostMinor = table.Column<long>(type: "INTEGER", nullable: false),
                IsArchived = table.Column<bool>(type: "INTEGER", nullable: false),
                Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PhysicalAssets", x => x.Id);
                table.CheckConstraint("CK_PhysicalAssets_Quantity_Positive", "Quantity > 0");
                table.CheckConstraint("CK_PhysicalAssets_Cost_NonNegative", "AcquisitionCostMinor >= 0");
                table.CheckConstraint("CK_PhysicalAssets_Currency_Length", "length(CurrencyCode) = 3");
            });

        migrationBuilder.CreateTable(
            name: "AssetValuations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                AssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                ValuationDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                ValueMinor = table.Column<long>(type: "INTEGER", nullable: false),
                CurrencyCode = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                Note = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AssetValuations", x => x.Id);
                table.CheckConstraint("CK_AssetValuations_Value_NonNegative", "ValueMinor >= 0");
                table.CheckConstraint("CK_AssetValuations_Currency_Length", "length(CurrencyCode) = 3");
                table.ForeignKey("FK_AssetValuations_PhysicalAssets_AssetId", x => x.AssetId, "PhysicalAssets", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_PhysicalAssets_IsArchived_Name", "PhysicalAssets", new[] { "IsArchived", "Name" });
        migrationBuilder.CreateIndex("IX_AssetValuations_AssetId_ValuationDate", "AssetValuations", new[] { "AssetId", "ValuationDate" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("AssetValuations");
        migrationBuilder.DropTable("PhysicalAssets");
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder) =>
        new AppDbContextModelSnapshot().BuildTargetModelForMigration(modelBuilder);
}
