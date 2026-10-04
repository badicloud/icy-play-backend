using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IcyPlay.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOpenPlayCheckIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CheckInOpensMinutes",
                schema: "dbo",
                table: "OpenPlays",
                type: "int",
                nullable: false,
                // OpenPlayLimits.DefaultCheckInLeadMinutes: open plays made before
                // check-in existed open their door an hour before start.
                defaultValue: 60);

            migrationBuilder.AddColumn<string>(
                name: "CheckInToken",
                schema: "dbo",
                table: "OpenPlayRegistrations",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CheckedInAt",
                schema: "dbo",
                table: "OpenPlayRegistrations",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CheckedInByUserId",
                schema: "dbo",
                table: "OpenPlayRegistrations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpenPlayRegistrations_CheckInToken",
                schema: "dbo",
                table: "OpenPlayRegistrations",
                column: "CheckInToken",
                unique: true,
                filter: "[CheckInToken] IS NOT NULL");

            // Registrations confirmed before check-in existed get their QR too:
            // two random GUIDs, 64 hex characters, unguessable like the 43
            // base64url ones the app makes. 3 is BookingStatus.Confirmed.
            migrationBuilder.Sql(
                """
                UPDATE [dbo].[OpenPlayRegistrations]
                SET [CheckInToken] = LOWER(REPLACE(CONVERT(nvarchar(36), NEWID()), '-', '')
                    + REPLACE(CONVERT(nvarchar(36), NEWID()), '-', ''))
                WHERE [Status] = 3 AND [CheckInToken] IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OpenPlayRegistrations_CheckInToken",
                schema: "dbo",
                table: "OpenPlayRegistrations");

            migrationBuilder.DropColumn(
                name: "CheckInOpensMinutes",
                schema: "dbo",
                table: "OpenPlays");

            migrationBuilder.DropColumn(
                name: "CheckInToken",
                schema: "dbo",
                table: "OpenPlayRegistrations");

            migrationBuilder.DropColumn(
                name: "CheckedInAt",
                schema: "dbo",
                table: "OpenPlayRegistrations");

            migrationBuilder.DropColumn(
                name: "CheckedInByUserId",
                schema: "dbo",
                table: "OpenPlayRegistrations");
        }
    }
}
