using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IcyPlay.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPhotoSport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Photos_CourtId",
                schema: "dbo",
                table: "Photos");

            migrationBuilder.AddColumn<Guid>(
                name: "SportId",
                schema: "dbo",
                table: "Photos",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Photos_CourtId_SportId",
                schema: "dbo",
                table: "Photos",
                columns: new[] { "CourtId", "SportId" });

            migrationBuilder.CreateIndex(
                name: "IX_Photos_SportId",
                schema: "dbo",
                table: "Photos",
                column: "SportId");

            migrationBuilder.AddForeignKey(
                name: "FK_Photos_Sports_SportId",
                schema: "dbo",
                table: "Photos",
                column: "SportId",
                principalSchema: "dbo",
                principalTable: "Sports",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Photos_Sports_SportId",
                schema: "dbo",
                table: "Photos");

            migrationBuilder.DropIndex(
                name: "IX_Photos_CourtId_SportId",
                schema: "dbo",
                table: "Photos");

            migrationBuilder.DropIndex(
                name: "IX_Photos_SportId",
                schema: "dbo",
                table: "Photos");

            migrationBuilder.DropColumn(
                name: "SportId",
                schema: "dbo",
                table: "Photos");

            migrationBuilder.CreateIndex(
                name: "IX_Photos_CourtId",
                schema: "dbo",
                table: "Photos",
                column: "CourtId");
        }
    }
}
