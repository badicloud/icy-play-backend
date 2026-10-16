using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IcyPlay.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOpenPlaySessionEnd : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EndedAt",
                schema: "dbo",
                table: "OpenPlaySessions",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EndedByUserId",
                schema: "dbo",
                table: "OpenPlaySessions",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EndedAt",
                schema: "dbo",
                table: "OpenPlaySessions");

            migrationBuilder.DropColumn(
                name: "EndedByUserId",
                schema: "dbo",
                table: "OpenPlaySessions");
        }
    }
}
