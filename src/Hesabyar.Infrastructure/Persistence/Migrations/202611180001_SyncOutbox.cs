using Hesabyar.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hesabyar.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("202611180001_SyncOutbox")]
public sealed class SyncOutbox : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SyncOutbox",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                AggregateId = table.Column<Guid>(type: "TEXT", nullable: false),
                AggregateType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                Operation = table.Column<int>(type: "INTEGER", nullable: false),
                SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                ChangedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                DeviceSequence = table.Column<long>(type: "INTEGER", nullable: false),
                FieldsJson = table.Column<string>(type: "TEXT", nullable: false),
                AcknowledgedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_SyncOutbox", x => x.Id));

        migrationBuilder.CreateIndex("IX_SyncOutbox_DeviceSequence", "SyncOutbox", "DeviceSequence", unique: true);
        migrationBuilder.CreateIndex("IX_SyncOutbox_AcknowledgedAtUtc_DeviceSequence", "SyncOutbox",
            new[] { "AcknowledgedAtUtc", "DeviceSequence" });
        migrationBuilder.CreateIndex("IX_SyncOutbox_AggregateType_AggregateId_DeviceSequence", "SyncOutbox",
            new[] { "AggregateType", "AggregateId", "DeviceSequence" });
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "SyncOutbox");

    protected override void BuildTargetModel(ModelBuilder modelBuilder) =>
        new AppDbContextModelSnapshot().BuildTargetModelForMigration(modelBuilder);
}
