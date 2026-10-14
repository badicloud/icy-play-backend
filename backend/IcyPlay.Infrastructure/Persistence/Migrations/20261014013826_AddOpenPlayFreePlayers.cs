using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IcyPlay.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOpenPlayFreePlayers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FreeSpots",
                schema: "dbo",
                table: "OpenPlays",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "OpenPlayFreePlayers",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Mobile = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    AddedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CheckedInAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CheckedInByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpenPlayFreePlayers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpenPlayFreePlayers_OpenPlaySessions_SessionId",
                        column: x => x.SessionId,
                        principalSchema: "dbo",
                        principalTable: "OpenPlaySessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OpenPlayFreePlayers_SessionId",
                schema: "dbo",
                table: "OpenPlayFreePlayers",
                column: "SessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OpenPlayFreePlayers",
                schema: "dbo");

            migrationBuilder.DropColumn(
                name: "FreeSpots",
                schema: "dbo",
                table: "OpenPlays");
        }
    }
}
