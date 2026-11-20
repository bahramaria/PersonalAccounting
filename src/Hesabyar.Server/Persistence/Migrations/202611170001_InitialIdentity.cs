using Hesabyar.Server.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Hesabyar.Server.Persistence.Migrations;

[DbContext(typeof(IdentityDbContext))]
[Migration("202611170001_InitialIdentity")]
public sealed class InitialIdentity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PhoneOtpRateLimits",
            columns: table => new
            {
                PhoneNumber = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                WindowStartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                RequestCount = table.Column<int>(type: "int", nullable: false),
                LastRequestedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PhoneOtpRateLimits", x => x.PhoneNumber);
                table.CheckConstraint("CK_PhoneOtpRateLimits_RequestCount_NonNegative", "[RequestCount] >= 0");
            });

        migrationBuilder.CreateTable(
            name: "PhoneOtpChallenges",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PhoneNumber = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                CodeHash = table.Column<byte[]>(type: "varbinary(32)", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                FailedAttempts = table.Column<int>(type: "int", nullable: false),
                ConsumedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                CancelledAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_PhoneOtpChallenges", x => x.Id));
        migrationBuilder.CreateIndex("IX_PhoneOtpChallenges_ExpiresAtUtc", "PhoneOtpChallenges", "ExpiresAtUtc");
        migrationBuilder.CreateIndex("IX_PhoneOtpChallenges_PhoneNumber_CreatedAtUtc",
            "PhoneOtpChallenges", new[] { "PhoneNumber", "CreatedAtUtc" });

        migrationBuilder.CreateTable(
            name: "Users",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PhoneNumber = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Users", x => x.Id));
        migrationBuilder.CreateIndex("IX_Users_PhoneNumber", "Users", "PhoneNumber", unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("PhoneOtpChallenges");
        migrationBuilder.DropTable("PhoneOtpRateLimits");
        migrationBuilder.DropTable("Users");
    }
}
