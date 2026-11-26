using Hesabyar.Server.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Hesabyar.Server.Persistence.Migrations;

[DbContext(typeof(IdentityDbContext))]
[Migration("202611170002_RefreshTokenSessions")]
public sealed class RefreshTokenSessions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "RefreshTokenSessions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                FamilyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                DeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                DeviceName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                TokenHash = table.Column<byte[]>(type: "varbinary(32)", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                RevokedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                ReplacedByTokenId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RefreshTokenSessions", x => x.Id);
                table.ForeignKey(
                    name: "FK_RefreshTokenSessions_Users_UserId",
                    column: x => x.UserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_RefreshTokenSessions_TokenHash",
            table: "RefreshTokenSessions",
            column: "TokenHash",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_RefreshTokenSessions_UserId_DeviceId",
            table: "RefreshTokenSessions",
            columns: new[] { "UserId", "DeviceId" });
        migrationBuilder.CreateIndex(
            name: "IX_RefreshTokenSessions_FamilyId",
            table: "RefreshTokenSessions",
            column: "FamilyId");
        migrationBuilder.CreateIndex(
            name: "IX_RefreshTokenSessions_ExpiresAtUtc",
            table: "RefreshTokenSessions",
            column: "ExpiresAtUtc");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "RefreshTokenSessions");
}
