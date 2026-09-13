using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace IcyPlay.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCourtsAndSports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Courts",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FacilityOwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    VenueType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Surface = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    HasLighting = table.Column<bool>(type: "bit", nullable: false),
                    SizeLabel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Capacity = table.Column<int>(type: "int", nullable: true),
                    Equipment = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SlotLengthMinutes = table.Column<int>(type: "int", nullable: false),
                    MinimumDurationMinutes = table.Column<int>(type: "int", nullable: false),
                    BufferMinutes = table.Column<int>(type: "int", nullable: false),
                    UsesFacilityHours = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Courts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Courts_Facilities_FacilityId",
                        column: x => x.FacilityId,
                        principalSchema: "dbo",
                        principalTable: "Facilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Sports",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CourtOperatingHours",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CourtId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DayOfWeek = table.Column<int>(type: "int", nullable: false),
                    OpensAt = table.Column<TimeOnly>(type: "time", nullable: true),
                    ClosesAt = table.Column<TimeOnly>(type: "time", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourtOperatingHours", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourtOperatingHours_Courts_CourtId",
                        column: x => x.CourtId,
                        principalSchema: "dbo",
                        principalTable: "Courts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MaintenancePeriods",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CourtId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StartsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SetByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LiftedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenancePeriods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaintenancePeriods_Courts_CourtId",
                        column: x => x.CourtId,
                        principalSchema: "dbo",
                        principalTable: "Courts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MaintenancePeriods_Facilities_FacilityId",
                        column: x => x.FacilityId,
                        principalSchema: "dbo",
                        principalTable: "Facilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CourtSports",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CourtId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SportId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourtSports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourtSports_Courts_CourtId",
                        column: x => x.CourtId,
                        principalSchema: "dbo",
                        principalTable: "Courts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CourtSports_Sports_SportId",
                        column: x => x.SportId,
                        principalSchema: "dbo",
                        principalTable: "Sports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "dbo",
                table: "Sports",
                columns: new[] { "Id", "Category", "CreatedAt", "DisplayOrder", "IsActive", "Key", "Name", "UpdatedAt" },
                values: new object[,]
                {
                    { new Guid("0d37f0cd-9517-5a92-a320-caff82b9bcc6"), "Court sports", new DateTimeOffset(new DateTime(2026, 9, 11, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 30, true, "futsal", "Futsal", null },
                    { new Guid("10a110db-d802-51a0-ada1-8f81bc655dcd"), "Combat", new DateTimeOffset(new DateTime(2026, 9, 11, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 40, true, "muay-thai", "Muay Thai", null },
                    { new Guid("17b95f71-a2b5-50a7-9fde-b07562272bed"), "Racket sports", new DateTimeOffset(new DateTime(2026, 9, 11, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 30, true, "table-tennis", "Table tennis", null },
                    { new Guid("2722a749-7853-5d82-a472-5c1ce07862c4"), "Combat", new DateTimeOffset(new DateTime(2026, 9, 11, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 30, true, "karate", "Karate", null },
                    { new Guid("441948f8-b6cf-5a23-8074-b45e692b2d73"), "Other", new DateTimeOffset(new DateTime(2026, 9, 11, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 30, true, "yoga", "Yoga", null },
                    { new Guid("63573cf8-2b30-57f8-8ead-1da5098ea88e"), "Racket sports", new DateTimeOffset(new DateTime(2026, 9, 11, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 40, true, "pickleball", "Pickleball", null },
                    { new Guid("74ad5832-6d45-5076-8b4c-d0c79472dd55"), "Court sports", new DateTimeOffset(new DateTime(2026, 9, 11, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 10, true, "basketball", "Basketball", null },
                    { new Guid("84ed5c9c-3978-56d8-bc1d-9cac5ae1109e"), "Combat", new DateTimeOffset(new DateTime(2026, 9, 11, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 10, true, "boxing", "Boxing", null },
                    { new Guid("8bf0905e-65ba-5600-a868-0f28087f1d0b"), "Racket sports", new DateTimeOffset(new DateTime(2026, 9, 11, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 10, true, "badminton", "Badminton", null },
                    { new Guid("8e21e167-3c31-5e9e-b203-f94339286f02"), "Court sports", new DateTimeOffset(new DateTime(2026, 9, 11, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 20, true, "volleyball", "Volleyball", null },
                    { new Guid("a0f6b01f-491c-5bb0-ad8e-a42f19437ea3"), "Racket sports", new DateTimeOffset(new DateTime(2026, 9, 11, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 20, true, "tennis", "Tennis", null },
                    { new Guid("aee78495-e295-5753-8b47-de220d9d6f2b"), "Other", new DateTimeOffset(new DateTime(2026, 9, 11, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 20, true, "dance", "Dance", null },
                    { new Guid("babc048a-e8a3-5572-8f03-c5a6a7f57d3f"), "Other", new DateTimeOffset(new DateTime(2026, 9, 11, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 10, true, "fitness", "Fitness", null },
                    { new Guid("c1f88227-155e-5da6-b12b-479ed504e853"), "Court sports", new DateTimeOffset(new DateTime(2026, 9, 11, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 40, true, "sepak-takraw", "Sepak takraw", null },
                    { new Guid("dad7ebe3-f916-50f4-9317-92d01c75a061"), "Racket sports", new DateTimeOffset(new DateTime(2026, 9, 11, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 50, true, "squash", "Squash", null },
                    { new Guid("f22fc719-1486-5215-9cc9-ec04de119105"), "Combat", new DateTimeOffset(new DateTime(2026, 9, 11, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 20, true, "taekwondo", "Taekwondo", null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_CourtOperatingHours_CourtId_DayOfWeek",
                schema: "dbo",
                table: "CourtOperatingHours",
                columns: new[] { "CourtId", "DayOfWeek" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Courts_FacilityId_DisplayOrder",
                schema: "dbo",
                table: "Courts",
                columns: new[] { "FacilityId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_Courts_FacilityOwnerId",
                schema: "dbo",
                table: "Courts",
                column: "FacilityOwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_CourtSports_CourtId_SportId",
                schema: "dbo",
                table: "CourtSports",
                columns: new[] { "CourtId", "SportId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CourtSports_SportId",
                schema: "dbo",
                table: "CourtSports",
                column: "SportId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenancePeriods_CourtId",
                schema: "dbo",
                table: "MaintenancePeriods",
                column: "CourtId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenancePeriods_FacilityId_StartsAt_EndsAt",
                schema: "dbo",
                table: "MaintenancePeriods",
                columns: new[] { "FacilityId", "StartsAt", "EndsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Sports_Key",
                schema: "dbo",
                table: "Sports",
                column: "Key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CourtOperatingHours",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "CourtSports",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "MaintenancePeriods",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Sports",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Courts",
                schema: "dbo");
        }
    }
}
