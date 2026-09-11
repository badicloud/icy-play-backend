using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IcyPlay.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContractSignedAgreement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DocumentContentType",
                schema: "dbo",
                table: "FacilityOwnerContracts",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentFileName",
                schema: "dbo",
                table: "FacilityOwnerContracts",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentPublicId",
                schema: "dbo",
                table: "FacilityOwnerContracts",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentSecureUrl",
                schema: "dbo",
                table: "FacilityOwnerContracts",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DocumentSizeInBytes",
                schema: "dbo",
                table: "FacilityOwnerContracts",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DocumentContentType",
                schema: "dbo",
                table: "FacilityOwnerContracts");

            migrationBuilder.DropColumn(
                name: "DocumentFileName",
                schema: "dbo",
                table: "FacilityOwnerContracts");

            migrationBuilder.DropColumn(
                name: "DocumentPublicId",
                schema: "dbo",
                table: "FacilityOwnerContracts");

            migrationBuilder.DropColumn(
                name: "DocumentSecureUrl",
                schema: "dbo",
                table: "FacilityOwnerContracts");

            migrationBuilder.DropColumn(
                name: "DocumentSizeInBytes",
                schema: "dbo",
                table: "FacilityOwnerContracts");
        }
    }
}
