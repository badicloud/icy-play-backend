using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IcyPlay.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentIntents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChosenMethod",
                schema: "dbo",
                table: "OnlinePayments",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExpiresAt",
                schema: "dbo",
                table: "OnlinePayments",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FailureReason",
                schema: "dbo",
                table: "OnlinePayments",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NextActionUrl",
                schema: "dbo",
                table: "OnlinePayments",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentIntentId",
                schema: "dbo",
                table: "OnlinePayments",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QrCodeImage",
                schema: "dbo",
                table: "OnlinePayments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "QuotedTotal",
                schema: "dbo",
                table: "OnlinePayments",
                type: "decimal(10,2)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OnlinePayments_PaymentIntentId",
                schema: "dbo",
                table: "OnlinePayments",
                column: "PaymentIntentId",
                unique: true,
                filter: "[PaymentIntentId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OnlinePayments_PaymentIntentId",
                schema: "dbo",
                table: "OnlinePayments");

            migrationBuilder.DropColumn(
                name: "ChosenMethod",
                schema: "dbo",
                table: "OnlinePayments");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                schema: "dbo",
                table: "OnlinePayments");

            migrationBuilder.DropColumn(
                name: "FailureReason",
                schema: "dbo",
                table: "OnlinePayments");

            migrationBuilder.DropColumn(
                name: "NextActionUrl",
                schema: "dbo",
                table: "OnlinePayments");

            migrationBuilder.DropColumn(
                name: "PaymentIntentId",
                schema: "dbo",
                table: "OnlinePayments");

            migrationBuilder.DropColumn(
                name: "QrCodeImage",
                schema: "dbo",
                table: "OnlinePayments");

            migrationBuilder.DropColumn(
                name: "QuotedTotal",
                schema: "dbo",
                table: "OnlinePayments");
        }
    }
}
