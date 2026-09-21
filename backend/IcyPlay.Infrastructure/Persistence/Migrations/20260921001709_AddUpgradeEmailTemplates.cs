using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace IcyPlay.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUpgradeEmailTemplates : Migration
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
                    { new Guid("a19d4f62-7c85-4b03-9e2a-58d1b6f04c37"), new DateTimeOffset(new DateTime(2026, 9, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 8365289L, true, "booking-upgrade-submitted", "Mailjet", "{{var:customer_name}} has paid to move to {{var:to_court_name}}", null },
                    { new Guid("c60e9b28-3af1-4d74-b85c-2e7a91d5c803"), new DateTimeOffset(new DateTime(2026, 9, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 8365290L, true, "booking-upgrade-approved", "Mailjet", "Your booking has moved to {{var:to_court_name}}", null },
                    { new Guid("f5a81c30-6b47-4e92-8d15-c9f3a207e64b"), new DateTimeOffset(new DateTime(2026, 9, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 8365287L, true, "booking-upgrade-received", "Mailjet", "We have your payment — {{var:facility_name}} is checking your upgrade", null }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "EmailTemplates",
                keyColumn: "Id",
                keyValue: new Guid("a19d4f62-7c85-4b03-9e2a-58d1b6f04c37"));

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "EmailTemplates",
                keyColumn: "Id",
                keyValue: new Guid("c60e9b28-3af1-4d74-b85c-2e7a91d5c803"));

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "EmailTemplates",
                keyColumn: "Id",
                keyValue: new Guid("f5a81c30-6b47-4e92-8d15-c9f3a207e64b"));
        }
    }
}
