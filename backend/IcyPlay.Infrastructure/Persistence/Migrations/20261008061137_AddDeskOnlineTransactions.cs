using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IcyPlay.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeskOnlineTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                schema: "dbo",
                table: "OnlinePayments",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FacilityId",
                schema: "dbo",
                table: "OnlinePayments",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Payments made before the venue was kept on them: worked out from
            // what each paid for, so the desk lists them too.
            migrationBuilder.Sql(
                """
                UPDATE p SET p.FacilityId = c.FacilityId
                FROM dbo.OnlinePayments p
                JOIN dbo.Bookings b ON b.Id = p.SubjectId AND p.Purpose = 'Booking'
                JOIN dbo.BookableCourts u ON u.Id = b.BookableCourtId
                JOIN dbo.Courts c ON c.Id = u.CourtId;

                UPDATE p SET p.FacilityId = c.FacilityId
                FROM dbo.OnlinePayments p
                JOIN dbo.BookingUpgradeRequests r ON r.Id = p.SubjectId AND p.Purpose = 'BookingUpgrade'
                JOIN dbo.Bookings b ON b.Id = r.BookingId
                JOIN dbo.BookableCourts u ON u.Id = b.BookableCourtId
                JOIN dbo.Courts c ON c.Id = u.CourtId;

                UPDATE p SET p.FacilityId = o.FacilityId
                FROM dbo.OnlinePayments p
                JOIN dbo.OpenPlayRegistrations r ON r.Id = p.SubjectId AND p.Purpose = 'OpenPlayRegistration'
                JOIN dbo.OpenPlaySessions s ON s.Id = r.SessionId
                JOIN dbo.OpenPlays o ON o.Id = s.OpenPlayId;
                """);

            migrationBuilder.CreateTable(
                name: "DeskReadMarkers",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Feed = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SeenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeskReadMarkers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OnlinePayments_FacilityId_Status_PaidAt",
                schema: "dbo",
                table: "OnlinePayments",
                columns: new[] { "FacilityId", "Status", "PaidAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DeskReadMarkers_UserId_Feed",
                schema: "dbo",
                table: "DeskReadMarkers",
                columns: new[] { "UserId", "Feed" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeskReadMarkers",
                schema: "dbo");

            migrationBuilder.DropIndex(
                name: "IX_OnlinePayments_FacilityId_Status_PaidAt",
                schema: "dbo",
                table: "OnlinePayments");

            migrationBuilder.DropColumn(
                name: "Description",
                schema: "dbo",
                table: "OnlinePayments");

            migrationBuilder.DropColumn(
                name: "FacilityId",
                schema: "dbo",
                table: "OnlinePayments");
        }
    }
}
