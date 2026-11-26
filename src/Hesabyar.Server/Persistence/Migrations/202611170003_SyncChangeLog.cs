using Hesabyar.Server.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Hesabyar.Server.Persistence.Migrations;

[DbContext(typeof(IdentityDbContext))]
[Migration("202611170003_SyncChangeLog")]
public sealed class SyncChangeLog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SyncChangeLogs",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                DeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ChangeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AggregateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AggregateType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                DeviceSequence = table.Column<long>(type: "bigint", nullable: false),
                Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                ReceivedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SyncChangeLogs", x => x.Id);
                table.ForeignKey(
                    name: "FK_SyncChangeLogs_Users_UserId",
                    column: x => x.UserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.CreateIndex("IX_SyncChangeLogs_UserId_ChangeId", "SyncChangeLogs",
            new[] { "UserId", "ChangeId" }, unique: true);
        migrationBuilder.CreateIndex("IX_SyncChangeLogs_UserId_DeviceId_DeviceSequence", "SyncChangeLogs",
            new[] { "UserId", "DeviceId", "DeviceSequence" }, unique: true);
        migrationBuilder.CreateIndex("IX_SyncChangeLogs_UserId_Id", "SyncChangeLogs", new[] { "UserId", "Id" });
        migrationBuilder.CreateIndex("IX_SyncChangeLogs_UserId_AggregateId_Id", "SyncChangeLogs",
            new[] { "UserId", "AggregateId", "Id" });
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "SyncChangeLogs");
}
