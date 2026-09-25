using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IcyPlay.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingDeclinedEmailTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "dbo",
                table: "EmailTemplates",
                columns: new[] { "Id", "CreatedAt", "ExternalTemplateId", "IsActive", "Key", "Provider", "Subject", "UpdatedAt" },
                values: new object[] { new Guid("b83f6a17-2e49-4d05-9c61-f4a0d7e25b98"), new DateTimeOffset(new DateTime(2026, 9, 25, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 8379592L, true, "booking-declined", "Mailjet", "Your booking at {{var:facility_name}} was not accepted", null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "EmailTemplates",
                keyColumn: "Id",
                keyValue: new Guid("b83f6a17-2e49-4d05-9c61-f4a0d7e25b98"));
        }
    }
}
