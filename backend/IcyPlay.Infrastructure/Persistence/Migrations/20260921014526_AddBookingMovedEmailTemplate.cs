using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IcyPlay.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingMovedEmailTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "dbo",
                table: "EmailTemplates",
                columns: new[] { "Id", "CreatedAt", "ExternalTemplateId", "IsActive", "Key", "Provider", "Subject", "UpdatedAt" },
                values: new object[] { new Guid("7b3c05e1-9d46-4a28-83f7-1e5a2c904db6"), new DateTimeOffset(new DateTime(2026, 9, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 8365353L, true, "booking-moved", "Mailjet", "{{var:customer_name}} moved to {{var:to_court_name}} — no action needed", null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "EmailTemplates",
                keyColumn: "Id",
                keyValue: new Guid("7b3c05e1-9d46-4a28-83f7-1e5a2c904db6"));
        }
    }
}
