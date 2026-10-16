using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IcyPlay.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOpenPlayRegistrationOpenUntilEnd : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RegistrationOpenUntilEnd",
                schema: "dbo",
                table: "OpenPlays",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RegistrationOpenUntilEnd",
                schema: "dbo",
                table: "OpenPlays");
        }
    }
}
