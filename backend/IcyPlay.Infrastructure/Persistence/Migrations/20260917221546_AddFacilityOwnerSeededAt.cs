using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IcyPlay.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFacilityOwnerSeededAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SeededAt",
                schema: "dbo",
                table: "FacilityOwners",
                type: "datetimeoffset",
                nullable: true);

            // The venues the seeder raised before it started marking them.
            //
            // Matching on the name is exactly what the column exists to stop,
            // so this is written as tightly as the seeder's own naming allows —
            // the whole name, and six hexadecimal characters where the tag
            // goes — and it runs once, here, rather than becoming the way this
            // question is answered from now on. SeededAt takes the row's own
            // CreatedAt, so the marker says when the venue was actually built
            // rather than when this migration happened to run.
            migrationBuilder.Sql(
                """
                UPDATE dbo.FacilityOwners
                SET SeededAt = CreatedAt
                WHERE SeededAt IS NULL
                  AND BusinessName LIKE 'Demo Sports Ventures [0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f]';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_FacilityOwners_SeededAt",
                schema: "dbo",
                table: "FacilityOwners",
                column: "SeededAt",
                filter: "[SeededAt] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FacilityOwners_SeededAt",
                schema: "dbo",
                table: "FacilityOwners");

            migrationBuilder.DropColumn(
                name: "SeededAt",
                schema: "dbo",
                table: "FacilityOwners");
        }
    }
}
