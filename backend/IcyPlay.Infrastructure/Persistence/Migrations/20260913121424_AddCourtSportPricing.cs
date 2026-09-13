using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IcyPlay.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCourtSportPricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "HolidayRate",
                schema: "dbo",
                table: "CourtSports",
                type: "decimal(10,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PeakHourlyRate",
                schema: "dbo",
                table: "CourtSports",
                type: "decimal(10,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StandardHourlyRate",
                schema: "dbo",
                table: "CourtSports",
                type: "decimal(10,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WeekendRate",
                schema: "dbo",
                table: "CourtSports",
                type: "decimal(10,2)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HolidayRate",
                schema: "dbo",
                table: "CourtSports");

            migrationBuilder.DropColumn(
                name: "PeakHourlyRate",
                schema: "dbo",
                table: "CourtSports");

            migrationBuilder.DropColumn(
                name: "StandardHourlyRate",
                schema: "dbo",
                table: "CourtSports");

            migrationBuilder.DropColumn(
                name: "WeekendRate",
                schema: "dbo",
                table: "CourtSports");
        }
    }
}
