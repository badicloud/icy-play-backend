using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IcyPlay.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOpenPlayRegistrationReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OpenPlayRegistrations_CustomerUserId",
                schema: "dbo",
                table: "OpenPlayRegistrations");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AgreedToPolicyAt",
                schema: "dbo",
                table: "OpenPlayRegistrations",
                type: "datetimeoffset",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<Guid>(
                name: "ConfirmedByUserId",
                schema: "dbo",
                table: "OpenPlayRegistrations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RejectedByUserId",
                schema: "dbo",
                table: "OpenPlayRegistrations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionNote",
                schema: "dbo",
                table: "OpenPlayRegistrations",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                schema: "dbo",
                table: "OpenPlayRegistrations",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpenPlayRegistrations_CustomerUserId_SessionId",
                schema: "dbo",
                table: "OpenPlayRegistrations",
                columns: new[] { "CustomerUserId", "SessionId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OpenPlayRegistrations_CustomerUserId_SessionId",
                schema: "dbo",
                table: "OpenPlayRegistrations");

            migrationBuilder.DropColumn(
                name: "AgreedToPolicyAt",
                schema: "dbo",
                table: "OpenPlayRegistrations");

            migrationBuilder.DropColumn(
                name: "ConfirmedByUserId",
                schema: "dbo",
                table: "OpenPlayRegistrations");

            migrationBuilder.DropColumn(
                name: "RejectedByUserId",
                schema: "dbo",
                table: "OpenPlayRegistrations");

            migrationBuilder.DropColumn(
                name: "RejectionNote",
                schema: "dbo",
                table: "OpenPlayRegistrations");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                schema: "dbo",
                table: "OpenPlayRegistrations");

            migrationBuilder.CreateIndex(
                name: "IX_OpenPlayRegistrations_CustomerUserId",
                schema: "dbo",
                table: "OpenPlayRegistrations",
                column: "CustomerUserId");
        }
    }
}
