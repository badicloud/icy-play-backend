using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IcyPlay.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecordBookingMovesWithReasons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MoveReason",
                schema: "dbo",
                table: "BookingUpgradeRequests",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MoveReasonNote",
                schema: "dbo",
                table: "BookingUpgradeRequests",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BookingMoves",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ReasonNote = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    FromCourtName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ToCourtName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    MovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingMoves", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookingMoves_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalSchema: "dbo",
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookingMoves_BookingId",
                schema: "dbo",
                table: "BookingMoves",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_BookingMoves_MovedAt",
                schema: "dbo",
                table: "BookingMoves",
                column: "MovedAt");

            // Every move made before this table existed, taken from the audit
            // trail that already recorded each one — so a report of moves counts
            // from the first move rather than from the day this shipped.
            //
            // None of them has a reason, because nobody was asked; the report
            // shows them as not asked rather than guessing. Nor do they have the
            // court names: the trail kept those in a sentence, and parsing a
            // sentence back into columns is guessing too.
            //
            // An upgrade's trail entry was written by the desk that approved it,
            // so who moved it is left empty rather than recorded as the
            // attendant. A free move's entry was written by the customer.
            migrationBuilder.Sql(
                """
                INSERT INTO [dbo].[BookingMoves]
                    ([Id], [BookingId], [MovedAt], [Kind], [MovedByUserId], [CreatedAt])
                SELECT
                    NEWID(),
                    audit.[EntityId],
                    audit.[CreatedAt],
                    CASE audit.[Action] WHEN N'BookingUpgradeApproved' THEN N'Upgrade' ELSE N'Free' END,
                    CASE audit.[Action] WHEN N'BookingMoved' THEN audit.[ActorUserId] ELSE NULL END,
                    audit.[CreatedAt]
                FROM [dbo].[AuditLogs] AS audit
                WHERE audit.[EntityType] = N'Booking'
                  AND audit.[Action] IN (N'BookingMoved', N'BookingUpgradeApproved')
                  AND EXISTS (SELECT 1 FROM [dbo].[Bookings] AS booking WHERE booking.[Id] = audit.[EntityId]);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookingMoves",
                schema: "dbo");

            migrationBuilder.DropColumn(
                name: "MoveReason",
                schema: "dbo",
                table: "BookingUpgradeRequests");

            migrationBuilder.DropColumn(
                name: "MoveReasonNote",
                schema: "dbo",
                table: "BookingUpgradeRequests");
        }
    }
}
