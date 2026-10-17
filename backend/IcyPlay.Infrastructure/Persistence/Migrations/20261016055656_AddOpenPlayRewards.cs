using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IcyPlay.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOpenPlayRewards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RewardDiscountKind",
                schema: "dbo",
                table: "OpenPlays",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RewardDiscountValue",
                schema: "dbo",
                table: "OpenPlays",
                type: "decimal(10,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RewardKind",
                schema: "dbo",
                table: "OpenPlays",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RewardMaxDiscount",
                schema: "dbo",
                table: "OpenPlays",
                type: "decimal(10,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RewardPrize",
                schema: "dbo",
                table: "OpenPlays",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RewardQuantity",
                schema: "dbo",
                table: "OpenPlays",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RewardValidDays",
                schema: "dbo",
                table: "OpenPlays",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OpenPlayClaimStubs",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OpenPlayId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(9)", maxLength: 9, nullable: false),
                    Prize = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    CustomerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AssignedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClaimedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClaimedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpenPlayClaimStubs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpenPlayClaimStubs_OpenPlays_OpenPlayId",
                        column: x => x.OpenPlayId,
                        principalSchema: "dbo",
                        principalTable: "OpenPlays",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OpenPlayVouchers",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OpenPlayId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DiscountKind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DiscountValue = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    MaxDiscount = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RegistrationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UsedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpenPlayVouchers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpenPlayVouchers_OpenPlaySessions_SessionId",
                        column: x => x.SessionId,
                        principalSchema: "dbo",
                        principalTable: "OpenPlaySessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OpenPlayClaimStubs_Code",
                schema: "dbo",
                table: "OpenPlayClaimStubs",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpenPlayClaimStubs_OpenPlayId_CustomerUserId",
                schema: "dbo",
                table: "OpenPlayClaimStubs",
                columns: new[] { "OpenPlayId", "CustomerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_OpenPlayClaimStubs_SessionId",
                schema: "dbo",
                table: "OpenPlayClaimStubs",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_OpenPlayVouchers_CustomerUserId_FacilityId_Status",
                schema: "dbo",
                table: "OpenPlayVouchers",
                columns: new[] { "CustomerUserId", "FacilityId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_OpenPlayVouchers_SessionId_CustomerUserId",
                schema: "dbo",
                table: "OpenPlayVouchers",
                columns: new[] { "SessionId", "CustomerUserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OpenPlayClaimStubs",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "OpenPlayVouchers",
                schema: "dbo");

            migrationBuilder.DropColumn(
                name: "RewardDiscountKind",
                schema: "dbo",
                table: "OpenPlays");

            migrationBuilder.DropColumn(
                name: "RewardDiscountValue",
                schema: "dbo",
                table: "OpenPlays");

            migrationBuilder.DropColumn(
                name: "RewardKind",
                schema: "dbo",
                table: "OpenPlays");

            migrationBuilder.DropColumn(
                name: "RewardMaxDiscount",
                schema: "dbo",
                table: "OpenPlays");

            migrationBuilder.DropColumn(
                name: "RewardPrize",
                schema: "dbo",
                table: "OpenPlays");

            migrationBuilder.DropColumn(
                name: "RewardQuantity",
                schema: "dbo",
                table: "OpenPlays");

            migrationBuilder.DropColumn(
                name: "RewardValidDays",
                schema: "dbo",
                table: "OpenPlays");
        }
    }
}
