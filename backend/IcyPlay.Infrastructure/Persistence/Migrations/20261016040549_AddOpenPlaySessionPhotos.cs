using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IcyPlay.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOpenPlaySessionPhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OpenPlaySessionPhotos",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PublicId = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    SecureUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    TakenByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RetiredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpenPlaySessionPhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpenPlaySessionPhotos_OpenPlaySessions_SessionId",
                        column: x => x.SessionId,
                        principalSchema: "dbo",
                        principalTable: "OpenPlaySessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OpenPlaySessionPhotos_ExpiresAt",
                schema: "dbo",
                table: "OpenPlaySessionPhotos",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_OpenPlaySessionPhotos_SessionId_Kind",
                schema: "dbo",
                table: "OpenPlaySessionPhotos",
                columns: new[] { "SessionId", "Kind" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OpenPlaySessionPhotos",
                schema: "dbo");
        }
    }
}
