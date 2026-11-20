using Hesabyar.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hesabyar.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("202611190002_DocumentAttachments")]
public sealed class DocumentAttachments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "DocumentAttachments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                TransactionId = table.Column<Guid>(type: "TEXT", nullable: true),
                PhysicalAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                OriginalFileName = table.Column<string>(type: "TEXT", maxLength: 180, nullable: false),
                StoredFileName = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                ContentType = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                Length = table.Column<long>(type: "INTEGER", nullable: false),
                Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DocumentAttachments", x => x.Id);
                table.CheckConstraint("CK_DocumentAttachments_ExactlyOneOwner",
                    "(TransactionId IS NOT NULL AND PhysicalAssetId IS NULL) OR (TransactionId IS NULL AND PhysicalAssetId IS NOT NULL)");
                table.CheckConstraint("CK_DocumentAttachments_Length", "Length > 0 AND Length <= 15728640");
                table.ForeignKey("FK_DocumentAttachments_Transactions_TransactionId",
                    x => x.TransactionId, "Transactions", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_DocumentAttachments_PhysicalAssets_PhysicalAssetId",
                    x => x.PhysicalAssetId, "PhysicalAssets", "Id", onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("IX_DocumentAttachments_TransactionId", "DocumentAttachments", "TransactionId");
        migrationBuilder.CreateIndex("IX_DocumentAttachments_PhysicalAssetId", "DocumentAttachments", "PhysicalAssetId");
        migrationBuilder.CreateIndex("IX_DocumentAttachments_StoredFileName", "DocumentAttachments", "StoredFileName", unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable("DocumentAttachments");

    protected override void BuildTargetModel(ModelBuilder modelBuilder) =>
        new AppDbContextModelSnapshot().BuildTargetModelForMigration(modelBuilder);
}