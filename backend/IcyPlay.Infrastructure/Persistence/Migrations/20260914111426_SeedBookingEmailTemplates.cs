using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace IcyPlay.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeedBookingEmailTemplates : Migration
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
                    { new Guid("6e94a20d-81cf-4b35-a7e8-3c5d90f61b28"), new DateTimeOffset(new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 8347573L, true, "booking-payment-submitted", "Mailjet", "{{var:customer_name}} has paid for {{var:court_name}} — please confirm", null },
                    { new Guid("b3f61c47-5d28-4e0a-9c73-8a1e5b2049df"), new DateTimeOffset(new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 8347566L, true, "booking-payment-received", "Mailjet", "We have your payment — {{var:facility_name}} is checking it", null },
                    { new Guid("d27b5e93-40a6-4c18-b9f2-71e8c3a56d04"), new DateTimeOffset(new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 8347583L, true, "booking-confirmed", "Mailjet", "Your court at {{var:facility_name}} is confirmed", null }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "EmailTemplates",
                keyColumn: "Id",
                keyValue: new Guid("6e94a20d-81cf-4b35-a7e8-3c5d90f61b28"));

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "EmailTemplates",
                keyColumn: "Id",
                keyValue: new Guid("b3f61c47-5d28-4e0a-9c73-8a1e5b2049df"));

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "EmailTemplates",
                keyColumn: "Id",
                keyValue: new Guid("d27b5e93-40a6-4c18-b9f2-71e8c3a56d04"));
        }
    }
}
