using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace IcyPlay.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHolidays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Holidays",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RepeatsAnnually = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Holidays", x => x.Id);
                });

            migrationBuilder.InsertData(
                schema: "dbo",
                table: "Holidays",
                columns: new[] { "Id", "CreatedAt", "Date", "IsActive", "Kind", "Name", "RepeatsAnnually", "UpdatedAt" },
                values: new object[,]
                {
                    { new Guid("4f6bd1a7-9b9c-5d3e-8a1f-11a2b3c4d5e6"), new DateTimeOffset(new DateTime(2026, 9, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new DateOnly(2026, 1, 1), true, "Regular", "New Year's Day", true, null },
                    { new Guid("5a7ce2b8-acad-5e4f-9b2a-22b3c4d5e6f7"), new DateTimeOffset(new DateTime(2026, 9, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new DateOnly(2026, 4, 9), true, "Regular", "Araw ng Kagitingan", true, null },
                    { new Guid("6b8df3c9-bdbe-5f50-ac3b-33c4d5e6f708"), new DateTimeOffset(new DateTime(2026, 9, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new DateOnly(2026, 5, 1), true, "Regular", "Labor Day", true, null },
                    { new Guid("7c9e04da-cecf-5061-bd4c-44d5e6f70819"), new DateTimeOffset(new DateTime(2026, 9, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new DateOnly(2026, 6, 12), true, "Regular", "Independence Day", true, null },
                    { new Guid("8daf15eb-dfd0-5172-ce5d-55e6f708192a"), new DateTimeOffset(new DateTime(2026, 9, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new DateOnly(2026, 11, 30), true, "Regular", "Bonifacio Day", true, null },
                    { new Guid("9eb026fc-e0e1-5283-df6e-66f708192a3b"), new DateTimeOffset(new DateTime(2026, 9, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new DateOnly(2026, 12, 25), true, "Regular", "Christmas Day", true, null },
                    { new Guid("afc1370d-f1f2-5394-e07f-7708192a3b4c"), new DateTimeOffset(new DateTime(2026, 9, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new DateOnly(2026, 12, 30), true, "Regular", "Rizal Day", true, null },
                    { new Guid("b0d2481e-0203-54a5-f180-88192a3b4c5d"), new DateTimeOffset(new DateTime(2026, 9, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new DateOnly(2026, 8, 21), true, "Special non-working", "Ninoy Aquino Day", true, null },
                    { new Guid("c1e3592f-1314-55b6-0291-992a3b4c5d6e"), new DateTimeOffset(new DateTime(2026, 9, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new DateOnly(2026, 11, 1), true, "Special non-working", "All Saints' Day", true, null },
                    { new Guid("d2f46a30-2425-56c7-13a2-aa3b4c5d6e7f"), new DateTimeOffset(new DateTime(2026, 9, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new DateOnly(2026, 12, 8), true, "Special non-working", "Feast of the Immaculate Conception", true, null },
                    { new Guid("e3057b41-3536-57d8-24b3-bb4c5d6e7f80"), new DateTimeOffset(new DateTime(2026, 9, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new DateOnly(2026, 12, 31), true, "Special non-working", "Last day of the year", true, null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Holidays_Date",
                schema: "dbo",
                table: "Holidays",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_Holidays_Name_Date",
                schema: "dbo",
                table: "Holidays",
                columns: new[] { "Name", "Date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Holidays",
                schema: "dbo");
        }
    }
}
