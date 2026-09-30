using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IcyPlay.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOpenPlays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OpenPlays",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookableCourtId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CourtId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Level = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    MaxPlayers = table.Column<int>(type: "int", nullable: false),
                    RegistrationFee = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    StartsAt = table.Column<TimeOnly>(type: "time", nullable: false),
                    EndsAt = table.Column<TimeOnly>(type: "time", nullable: false),
                    Days = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    RegistrationCutoffMinutes = table.Column<int>(type: "int", nullable: false),
                    EarlyBirdDiscountKind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    EarlyBirdDiscountValue = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    EarlyBirdLeadMinutes = table.Column<int>(type: "int", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpenPlays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpenPlays_BookableCourts_BookableCourtId",
                        column: x => x.BookableCourtId,
                        principalSchema: "dbo",
                        principalTable: "BookableCourts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OpenPlays_Facilities_FacilityId",
                        column: x => x.FacilityId,
                        principalSchema: "dbo",
                        principalTable: "Facilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OpenPlaySessions",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OpenPlayId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    CancelledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancelledByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpenPlaySessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpenPlaySessions_OpenPlays_OpenPlayId",
                        column: x => x.OpenPlayId,
                        principalSchema: "dbo",
                        principalTable: "OpenPlays",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OpenPlayRegistrations",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RegistrationFee = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    Discount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    PlatformFee = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    HoldsUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReceiptUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReceiptUploadedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SubmittedForVerificationAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpenPlayRegistrations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpenPlayRegistrations_OpenPlaySessions_SessionId",
                        column: x => x.SessionId,
                        principalSchema: "dbo",
                        principalTable: "OpenPlaySessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OpenPlayRegistrations_CustomerUserId",
                schema: "dbo",
                table: "OpenPlayRegistrations",
                column: "CustomerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OpenPlayRegistrations_SessionId_Status",
                schema: "dbo",
                table: "OpenPlayRegistrations",
                columns: new[] { "SessionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_OpenPlays_BookableCourtId",
                schema: "dbo",
                table: "OpenPlays",
                column: "BookableCourtId");

            migrationBuilder.CreateIndex(
                name: "IX_OpenPlays_CourtId_StartDate_EndDate",
                schema: "dbo",
                table: "OpenPlays",
                columns: new[] { "CourtId", "StartDate", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_OpenPlays_FacilityId",
                schema: "dbo",
                table: "OpenPlays",
                column: "FacilityId");

            migrationBuilder.CreateIndex(
                name: "IX_OpenPlaySessions_OpenPlayId_Date",
                schema: "dbo",
                table: "OpenPlaySessions",
                columns: new[] { "OpenPlayId", "Date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OpenPlayRegistrations",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "OpenPlaySessions",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "OpenPlays",
                schema: "dbo");
        }
    }
}
