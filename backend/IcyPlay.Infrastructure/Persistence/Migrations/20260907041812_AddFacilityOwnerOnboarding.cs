using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IcyPlay.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFacilityOwnerOnboarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BusinessRegistrationNumber",
                schema: "dbo",
                table: "FacilityOwners",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FacilityOwnerContracts",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FacilityOwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CommencedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FacilityOwnerContracts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FacilityOwnerContracts_FacilityOwners_FacilityOwnerId",
                        column: x => x.FacilityOwnerId,
                        principalSchema: "dbo",
                        principalTable: "FacilityOwners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FacilityOwnerDocuments",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FacilityOwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PublicId = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    SecureUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SizeInBytes = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FacilityOwnerDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FacilityOwnerDocuments_FacilityOwners_FacilityOwnerId",
                        column: x => x.FacilityOwnerId,
                        principalSchema: "dbo",
                        principalTable: "FacilityOwners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FacilityOwnerContracts_FacilityOwnerId_StartDate_EndDate",
                schema: "dbo",
                table: "FacilityOwnerContracts",
                columns: new[] { "FacilityOwnerId", "StartDate", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_FacilityOwnerDocuments_FacilityOwnerId_DocumentType",
                schema: "dbo",
                table: "FacilityOwnerDocuments",
                columns: new[] { "FacilityOwnerId", "DocumentType" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FacilityOwnerContracts",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "FacilityOwnerDocuments",
                schema: "dbo");

            migrationBuilder.DropColumn(
                name: "BusinessRegistrationNumber",
                schema: "dbo",
                table: "FacilityOwners");
        }
    }
}
