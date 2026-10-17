using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IcyPlay.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOpenPlayRegistrationVoucher : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "VoucherDiscount",
                schema: "dbo",
                table: "OpenPlayRegistrations",
                type: "decimal(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "VoucherId",
                schema: "dbo",
                table: "OpenPlayRegistrations",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VoucherDiscount",
                schema: "dbo",
                table: "OpenPlayRegistrations");

            migrationBuilder.DropColumn(
                name: "VoucherId",
                schema: "dbo",
                table: "OpenPlayRegistrations");
        }
    }
}
