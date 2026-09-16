using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IcyPlay.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingMoveRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "PartialBookingExpiryMinutes",
                schema: "dbo",
                table: "FacilityOwners",
                type: "int",
                nullable: false,
                defaultValue: 5,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 30);

            migrationBuilder.AddColumn<int>(
                name: "MoveLimit",
                schema: "dbo",
                table: "FacilityOwners",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "BookableCourtId",
                schema: "dbo",
                table: "BookingSlots",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<decimal>(
                name: "PaidTotal",
                schema: "dbo",
                table: "Bookings",
                type: "decimal(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "BookingMoveRequests",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToBookableCourtId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToCourtName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Initiator = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PaidBefore = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    NewTotal = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    BalanceDue = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    HoldsUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReceiptUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReceiptUploadedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SettledByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SettledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    WaivedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WaiverReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DeclineReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingMoveRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookingMoveRequests_BookableCourts_ToBookableCourtId",
                        column: x => x.ToBookableCourtId,
                        principalSchema: "dbo",
                        principalTable: "BookableCourts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BookingMoveRequests_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalSchema: "dbo",
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookingSlots_BookableCourtId",
                schema: "dbo",
                table: "BookingSlots",
                column: "BookableCourtId");

            migrationBuilder.CreateIndex(
                name: "IX_BookingMoveRequests_BookingId_Status",
                schema: "dbo",
                table: "BookingMoveRequests",
                columns: new[] { "BookingId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_BookingMoveRequests_ToBookableCourtId_Status",
                schema: "dbo",
                table: "BookingMoveRequests",
                columns: new[] { "ToBookableCourtId", "Status" });

            // Every hour already in the table was sold on the court its booking
            // names, which is where that column used to be read from. Without
            // this they would all be left pointing at nothing and the foreign
            // key below would refuse to be created.
            migrationBuilder.Sql(@"
                UPDATE slot
                SET slot.BookableCourtId = booking.BookableCourtId
                FROM dbo.BookingSlots slot
                INNER JOIN dbo.Bookings booking ON booking.Id = slot.BookingId;");

            // A confirmed booking has been paid for, and what it was paid is
            // what its hours came to. Left at zero, the first upgrade on an old
            // booking would ask the customer for the whole of the new court
            // rather than the difference.
            migrationBuilder.Sql(@"
                UPDATE booking
                SET booking.PaidTotal = ISNULL(paid.Total, 0)
                FROM dbo.Bookings booking
                OUTER APPLY (
                    SELECT SUM(slot.Amount + slot.PlatformFee) AS Total
                    FROM dbo.BookingSlots slot
                    WHERE slot.BookingId = booking.Id
                ) paid
                WHERE booking.Status = 3;  -- BookingStatus.Confirmed, numbered explicitly in the enum");

            migrationBuilder.AddForeignKey(
                name: "FK_BookingSlots_BookableCourts_BookableCourtId",
                schema: "dbo",
                table: "BookingSlots",
                column: "BookableCourtId",
                principalSchema: "dbo",
                principalTable: "BookableCourts",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BookingSlots_BookableCourts_BookableCourtId",
                schema: "dbo",
                table: "BookingSlots");

            migrationBuilder.DropTable(
                name: "BookingMoveRequests",
                schema: "dbo");

            migrationBuilder.DropIndex(
                name: "IX_BookingSlots_BookableCourtId",
                schema: "dbo",
                table: "BookingSlots");

            migrationBuilder.DropColumn(
                name: "MoveLimit",
                schema: "dbo",
                table: "FacilityOwners");

            migrationBuilder.DropColumn(
                name: "BookableCourtId",
                schema: "dbo",
                table: "BookingSlots");

            migrationBuilder.DropColumn(
                name: "PaidTotal",
                schema: "dbo",
                table: "Bookings");

            migrationBuilder.AlterColumn<int>(
                name: "PartialBookingExpiryMinutes",
                schema: "dbo",
                table: "FacilityOwners",
                type: "int",
                nullable: false,
                defaultValue: 30,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 5);
        }
    }
}
