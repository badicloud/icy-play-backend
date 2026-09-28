using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace IcyPlay.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeedMoveRequestEmails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "dbo",
                table: "EmailTemplates",
                columns: new[] { "Id", "CreatedAt", "ExternalTemplateId", "IsActive", "Key", "Provider", "Subject", "UpdatedAt" },
                values: new object[,]
                {
                    { new Guid("3d8e1f52-a4c7-4b19-9e06-7f2b5c81d4a3"), new DateTimeOffset(new DateTime(2026, 9, 28, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 8384428L, true, "booking-move-requested", "Mailjet", "{{var:customer_name}} wants to move to {{var:to_court_name}} — waiting for you", null },
                    { new Guid("9a42c6e0-5b1d-4f83-a7e9-0c6d3b28f715"), new DateTimeOffset(new DateTime(2026, 9, 28, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 8384442L, true, "booking-move-approved", "Mailjet", "Your booking has moved to {{var:court_name}}", null },
                    { new Guid("e71b09d4-2c58-4a6f-b3d2-84f5a19c6e20"), new DateTimeOffset(new DateTime(2026, 9, 28, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 8384444L, true, "booking-move-declined", "Mailjet", "Your booking has not moved — you're still on {{var:court_name}}", null }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "EmailTemplates",
                keyColumn: "Id",
                keyValue: new Guid("3d8e1f52-a4c7-4b19-9e06-7f2b5c81d4a3"));

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "EmailTemplates",
                keyColumn: "Id",
                keyValue: new Guid("9a42c6e0-5b1d-4f83-a7e9-0c6d3b28f715"));

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "EmailTemplates",
                keyColumn: "Id",
                keyValue: new Guid("e71b09d4-2c58-4a6f-b3d2-84f5a19c6e20"));
        }
    }
}
