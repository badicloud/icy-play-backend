using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IcyPlay.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentDetailsAndBookingHold : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GcashAccountName",
                schema: "dbo",
                table: "FacilityOwners",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GcashNumber",
                schema: "dbo",
                table: "FacilityOwners",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GcashQrCodeUrl",
                schema: "dbo",
                table: "FacilityOwners",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PartialBookingExpiryMinutes",
                schema: "dbo",
                table: "FacilityOwners",
                type: "int",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<DateOnly>(
                name: "EndDate",
                schema: "dbo",
                table: "Bookings",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "HoldsUntil",
                schema: "dbo",
                table: "Bookings",
                type: "datetimeoffset",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReceiptUploadedAt",
                schema: "dbo",
                table: "Bookings",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReceiptUrl",
                schema: "dbo",
                table: "Bookings",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "StartDate",
                schema: "dbo",
                table: "Bookings",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SubmittedForVerificationAt",
                schema: "dbo",
                table: "Bookings",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_StartDate_EndDate",
                schema: "dbo",
                table: "Bookings",
                columns: new[] { "StartDate", "EndDate" });

            // Bookings that predate these columns would otherwise carry
            // 0001-01-01: nonsense dates, and a hold that ran out two thousand
            // years ago. Their dates come from the hours they already hold, and
            // their hold is measured from when they were taken, by the same rule
            // as any new one.
            //
            // One that was never paid for therefore reads as lapsed and its
            // hours go back on sale, which is the right answer: nobody paid.
            migrationBuilder.Sql("""
                UPDATE b
                SET b.StartDate  = hours.FirstDate,
                    b.EndDate    = hours.LastDate,
                    b.HoldsUntil = DATEADD(minute, 30, b.CreatedAt)
                FROM dbo.Bookings b
                CROSS APPLY (
                    SELECT MIN(s.Date) AS FirstDate, MAX(s.Date) AS LastDate
                    FROM dbo.BookingSlots s
                    WHERE s.BookingId = b.Id
                ) hours
                WHERE hours.FirstDate IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Bookings_StartDate_EndDate",
                schema: "dbo",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "GcashAccountName",
                schema: "dbo",
                table: "FacilityOwners");

            migrationBuilder.DropColumn(
                name: "GcashNumber",
                schema: "dbo",
                table: "FacilityOwners");

            migrationBuilder.DropColumn(
                name: "GcashQrCodeUrl",
                schema: "dbo",
                table: "FacilityOwners");

            migrationBuilder.DropColumn(
                name: "PartialBookingExpiryMinutes",
                schema: "dbo",
                table: "FacilityOwners");

            migrationBuilder.DropColumn(
                name: "EndDate",
                schema: "dbo",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "HoldsUntil",
                schema: "dbo",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "ReceiptUploadedAt",
                schema: "dbo",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "ReceiptUrl",
                schema: "dbo",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "StartDate",
                schema: "dbo",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "SubmittedForVerificationAt",
                schema: "dbo",
                table: "Bookings");
        }
    }
}
