using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IcyPlay.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingUpgradeRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BookingUpgradeRequests",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToBookableCourtId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToCourtName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RentalNow = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    RentalNew = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    BalanceDue = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    HoldsUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReceiptUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReceiptUploadedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SettledByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SettledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DeclineReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingUpgradeRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookingUpgradeRequests_BookableCourts_ToBookableCourtId",
                        column: x => x.ToBookableCourtId,
                        principalSchema: "dbo",
                        principalTable: "BookableCourts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BookingUpgradeRequests_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalSchema: "dbo",
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BookingUpgradeSlots",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    StartsAt = table.Column<TimeOnly>(type: "time", nullable: false),
                    EndsAt = table.Column<TimeOnly>(type: "time", nullable: false),
                    RateKind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    PlatformFee = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingUpgradeSlots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookingUpgradeSlots_BookingUpgradeRequests_RequestId",
                        column: x => x.RequestId,
                        principalSchema: "dbo",
                        principalTable: "BookingUpgradeRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookingUpgradeRequests_BookingId_Status",
                schema: "dbo",
                table: "BookingUpgradeRequests",
                columns: new[] { "BookingId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_BookingUpgradeRequests_ToBookableCourtId_Status",
                schema: "dbo",
                table: "BookingUpgradeRequests",
                columns: new[] { "ToBookableCourtId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_BookingUpgradeSlots_Date",
                schema: "dbo",
                table: "BookingUpgradeSlots",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_BookingUpgradeSlots_RequestId",
                schema: "dbo",
                table: "BookingUpgradeSlots",
                column: "RequestId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookingUpgradeSlots",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "BookingUpgradeRequests",
                schema: "dbo");
        }
    }
}
