using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IcyPlay.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Removes the table a court move used to wait in.
    ///
    /// A move is now made the moment it is asked for: the venue is not asked to
    /// approve it and no money changes hands on the way, so there is nothing
    /// left for a request to hold. Asking a venue to approve every move put a
    /// player who had already chosen a court into a queue, and a queue with
    /// nobody answering is a complaint for the venue — the opposite of what the
    /// feature was for.
    ///
    /// The rows go with it. They were requests waiting on an answer that can no
    /// longer be given, and the bookings they belonged to are untouched: a
    /// request never moved anything by itself.
    /// </summary>
    public partial class DropBookingMoveRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookingMoveRequests",
                schema: "dbo");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BookingMoveRequests",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToBookableCourtId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BalanceDue = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DeclineReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    HoldsUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Initiator = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    NewTotal = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    PaidBefore = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReceiptUploadedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReceiptUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SettledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SettledByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ToCourtName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    WaivedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WaiverReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
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
                name: "IX_BookingMoveRequests_BookingId_Status",
                schema: "dbo",
                table: "BookingMoveRequests",
                columns: new[] { "BookingId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_BookingMoveRequests_ToBookableCourtId_Status",
                schema: "dbo",
                table: "BookingMoveRequests",
                columns: new[] { "ToBookableCourtId", "Status" });
        }
    }
}
