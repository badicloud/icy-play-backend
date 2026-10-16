using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IcyPlay.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOpenPlayMatchResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Result",
                schema: "dbo",
                table: "OpenPlayMatches",
                type: "nvarchar(4)",
                maxLength: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ScoreA",
                schema: "dbo",
                table: "OpenPlayMatches",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ScoreB",
                schema: "dbo",
                table: "OpenPlayMatches",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Result",
                schema: "dbo",
                table: "OpenPlayMatches");

            migrationBuilder.DropColumn(
                name: "ScoreA",
                schema: "dbo",
                table: "OpenPlayMatches");

            migrationBuilder.DropColumn(
                name: "ScoreB",
                schema: "dbo",
                table: "OpenPlayMatches");
        }
    }
}
