using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IcyPlay.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentChannelToUpgradesAndOpenPlay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PaymentChannel",
                schema: "dbo",
                table: "OpenPlayRegistrations",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Manual");

            migrationBuilder.AddColumn<string>(
                name: "PaymentChannel",
                schema: "dbo",
                table: "BookingUpgradeRequests",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Manual");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaymentChannel",
                schema: "dbo",
                table: "OpenPlayRegistrations");

            migrationBuilder.DropColumn(
                name: "PaymentChannel",
                schema: "dbo",
                table: "BookingUpgradeRequests");
        }
    }
}
