using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IcyPlay.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCourtPeakWindow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeOnly>(
                name: "PeakEndsAt",
                schema: "dbo",
                table: "Courts",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PeakOnWeekdays",
                schema: "dbo",
                table: "Courts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PeakOnWeekends",
                schema: "dbo",
                table: "Courts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "PeakStartsAt",
                schema: "dbo",
                table: "Courts",
                type: "time",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PeakEndsAt",
                schema: "dbo",
                table: "Courts");

            migrationBuilder.DropColumn(
                name: "PeakOnWeekdays",
                schema: "dbo",
                table: "Courts");

            migrationBuilder.DropColumn(
                name: "PeakOnWeekends",
                schema: "dbo",
                table: "Courts");

            migrationBuilder.DropColumn(
                name: "PeakStartsAt",
                schema: "dbo",
                table: "Courts");
        }
    }
}
