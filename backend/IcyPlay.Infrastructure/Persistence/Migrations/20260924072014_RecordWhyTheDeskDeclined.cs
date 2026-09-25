using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IcyPlay.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecordWhyTheDeskDeclined : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RejectedByUserId",
                schema: "dbo",
                table: "Bookings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionNote",
                schema: "dbo",
                table: "Bookings",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                schema: "dbo",
                table: "Bookings",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            // Who turned down the refusals already made, from the audit trail —
            // the only place it was written. The reason stays null on those:
            // the desk typed rather than picked, and guessing a category from
            // what it typed would put words in its mouth. The report shows them
            // as not categorised, with the words it did write.
            migrationBuilder.Sql(
                """
                UPDATE booking
                SET booking.[RejectedByUserId] = (
                    SELECT TOP (1) audit.[ActorUserId]
                    FROM [dbo].[AuditLogs] AS audit
                    WHERE audit.[EntityType] = N'Booking'
                      AND audit.[Action] = N'BookingRejected'
                      AND audit.[EntityId] = booking.[Id]
                    ORDER BY audit.[CreatedAt] DESC)
                FROM [dbo].[Bookings] AS booking
                WHERE booking.[Status] = 4; -- BookingStatus.Rejected
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RejectedByUserId",
                schema: "dbo",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "RejectionNote",
                schema: "dbo",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                schema: "dbo",
                table: "Bookings");
        }
    }
}
