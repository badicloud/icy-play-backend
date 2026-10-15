using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IcyPlay.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOpenPlayMatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BackgroundPhotoPublicId",
                schema: "dbo",
                table: "OpenPlays",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BackgroundPhotoUrl",
                schema: "dbo",
                table: "OpenPlays",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PlayersPerTeam",
                schema: "dbo",
                table: "OpenPlays",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OpenPlayMatches",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    StartedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FinishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FinishedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpenPlayMatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpenPlayMatches_OpenPlaySessions_SessionId",
                        column: x => x.SessionId,
                        principalSchema: "dbo",
                        principalTable: "OpenPlaySessions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OpenPlayParticipants",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RegistrationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FreePlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    QueuedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DoneAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpenPlayParticipants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpenPlayParticipants_OpenPlaySessions_SessionId",
                        column: x => x.SessionId,
                        principalSchema: "dbo",
                        principalTable: "OpenPlaySessions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OpenPlayMatchSlots",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParticipantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Team = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    LeftAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpenPlayMatchSlots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpenPlayMatchSlots_OpenPlayMatches_MatchId",
                        column: x => x.MatchId,
                        principalSchema: "dbo",
                        principalTable: "OpenPlayMatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OpenPlayMatchSlots_OpenPlayParticipants_ParticipantId",
                        column: x => x.ParticipantId,
                        principalSchema: "dbo",
                        principalTable: "OpenPlayParticipants",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_OpenPlayMatches_SessionId_Number",
                schema: "dbo",
                table: "OpenPlayMatches",
                columns: new[] { "SessionId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpenPlayMatchSlots_MatchId",
                schema: "dbo",
                table: "OpenPlayMatchSlots",
                column: "MatchId");

            migrationBuilder.CreateIndex(
                name: "IX_OpenPlayMatchSlots_ParticipantId",
                schema: "dbo",
                table: "OpenPlayMatchSlots",
                column: "ParticipantId");

            migrationBuilder.CreateIndex(
                name: "IX_OpenPlayParticipants_SessionId_FreePlayerId",
                schema: "dbo",
                table: "OpenPlayParticipants",
                columns: new[] { "SessionId", "FreePlayerId" },
                unique: true,
                filter: "[FreePlayerId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OpenPlayParticipants_SessionId_RegistrationId",
                schema: "dbo",
                table: "OpenPlayParticipants",
                columns: new[] { "SessionId", "RegistrationId" },
                unique: true,
                filter: "[RegistrationId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OpenPlayMatchSlots",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "OpenPlayMatches",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "OpenPlayParticipants",
                schema: "dbo");

            migrationBuilder.DropColumn(
                name: "BackgroundPhotoPublicId",
                schema: "dbo",
                table: "OpenPlays");

            migrationBuilder.DropColumn(
                name: "BackgroundPhotoUrl",
                schema: "dbo",
                table: "OpenPlays");

            migrationBuilder.DropColumn(
                name: "PlayersPerTeam",
                schema: "dbo",
                table: "OpenPlays");
        }
    }
}
