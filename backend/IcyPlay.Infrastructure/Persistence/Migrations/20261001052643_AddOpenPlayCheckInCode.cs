using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IcyPlay.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOpenPlayCheckInCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CheckInCodeFailures",
                schema: "dbo",
                table: "FacilityOwners",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CheckInCodeLockedUntil",
                schema: "dbo",
                table: "FacilityOwners",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OpenPlayCheckInCodeHash",
                schema: "dbo",
                table: "FacilityOwners",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CheckInCodeFailures",
                schema: "dbo",
                table: "FacilityOwners");

            migrationBuilder.DropColumn(
                name: "CheckInCodeLockedUntil",
                schema: "dbo",
                table: "FacilityOwners");

            migrationBuilder.DropColumn(
                name: "OpenPlayCheckInCodeHash",
                schema: "dbo",
                table: "FacilityOwners");
        }
    }
}
