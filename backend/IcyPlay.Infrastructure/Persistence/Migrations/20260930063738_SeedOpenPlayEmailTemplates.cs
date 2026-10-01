using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace IcyPlay.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeedOpenPlayEmailTemplates : Migration
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
                    { new Guid("3c8e1f52-7a94-4d06-b1e3-5f20c9a8d471"), new DateTimeOffset(new DateTime(2026, 9, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 8392327L, true, "open-play-payment-received", "Mailjet", "We have your payment — {{var:facility_name}} is checking it", null },
                    { new Guid("6b40d8e3-91f7-4a25-bc68-2e5a7f14d903"), new DateTimeOffset(new DateTime(2026, 9, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 8392337L, true, "open-play-declined", "Mailjet", "Your registration for {{var:open_play_title}} was not accepted", null },
                    { new Guid("a5d27c09-3e61-4b8f-92a4-e07b16f3c582"), new DateTimeOffset(new DateTime(2026, 9, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 8392333L, true, "open-play-payment-submitted", "Mailjet", "{{var:player_name}} has paid to join {{var:open_play_title}} — please confirm", null },
                    { new Guid("f19b6e4a-08d3-4c72-a5e1-7b3d92c0f846"), new DateTimeOffset(new DateTime(2026, 9, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 8392336L, true, "open-play-confirmed", "Mailjet", "You're registered for {{var:open_play_title}} on {{var:session_date}}", null }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "EmailTemplates",
                keyColumn: "Id",
                keyValue: new Guid("3c8e1f52-7a94-4d06-b1e3-5f20c9a8d471"));

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "EmailTemplates",
                keyColumn: "Id",
                keyValue: new Guid("6b40d8e3-91f7-4a25-bc68-2e5a7f14d903"));

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "EmailTemplates",
                keyColumn: "Id",
                keyValue: new Guid("a5d27c09-3e61-4b8f-92a4-e07b16f3c582"));

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "EmailTemplates",
                keyColumn: "Id",
                keyValue: new Guid("f19b6e4a-08d3-4c72-a5e1-7b3d92c0f846"));
        }
    }
}
