using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IcyPlay.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookableCourts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BookableCourts",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CourtSportId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CourtId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DivisionNumber = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Whole"),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookableCourts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookableCourts_CourtSports_CourtSportId",
                        column: x => x.CourtSportId,
                        principalSchema: "dbo",
                        principalTable: "CourtSports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BookableCourts_Courts_CourtId",
                        column: x => x.CourtId,
                        principalSchema: "dbo",
                        principalTable: "Courts",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookableCourts_CourtId",
                schema: "dbo",
                table: "BookableCourts",
                column: "CourtId");

            migrationBuilder.CreateIndex(
                name: "IX_BookableCourts_CourtSportId_DivisionNumber",
                schema: "dbo",
                table: "BookableCourts",
                columns: new[] { "CourtSportId", "DivisionNumber" },
                unique: true);

            // Every court already configured, expanded into what it sells. The
            // listing reads this table from here on, so a court left without
            // rows would simply stop appearing.
            //
            // This repeats in SQL what BookableCourtRoster does in C#, which is
            // the one duplicate of the rule. It is deliberate: a migration is
            // frozen the moment it runs, so this copy cannot drift forward, and
            // an integration test holds the two to the same answer. The
            // alternative -- an empty table filled by something at startup --
            // reintroduces the "has this run yet?" question that migrations
            // exist to answer.
            migrationBuilder.Sql("""
                INSERT INTO dbo.BookableCourts
                    (Id, CourtSportId, CourtId, DivisionNumber, Kind, IsActive, CreatedAt)
                SELECT NEWID(),
                       cs.Id,
                       cs.CourtId,
                       parts.Number,
                       CASE WHEN cs.Divisions > 1 THEN 'Divided' ELSE 'Whole' END,
                       1,
                       SYSUTCDATETIME() AT TIME ZONE 'UTC'
                FROM dbo.CourtSports cs
                CROSS APPLY (
                    SELECT TOP (cs.Divisions)
                           ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS Number
                    FROM sys.all_objects
                ) parts;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookableCourts",
                schema: "dbo");
        }
    }
}
