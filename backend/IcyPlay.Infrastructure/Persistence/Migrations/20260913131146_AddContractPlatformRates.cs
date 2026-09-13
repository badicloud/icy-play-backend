using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IcyPlay.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContractPlatformRates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CommissionPercentage",
                schema: "dbo",
                table: "FacilityOwnerContracts",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 3.00m);

            migrationBuilder.AddColumn<decimal>(
                name: "PlatformHourlyRate",
                schema: "dbo",
                table: "FacilityOwnerContracts",
                type: "decimal(10,2)",
                nullable: false,
                defaultValue: 15.00m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CommissionPercentage",
                schema: "dbo",
                table: "FacilityOwnerContracts");

            migrationBuilder.DropColumn(
                name: "PlatformHourlyRate",
                schema: "dbo",
                table: "FacilityOwnerContracts");
        }
    }
}
