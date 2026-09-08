using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace IcyPlay.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFacilitiesAndAmenities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Amenities",
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
                    table.PrimaryKey("PK_Amenities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Facilities",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FacilityOwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(220)", maxLength: 220, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AddressLine1 = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    AddressLine2 = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Province = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PostalCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Country = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Latitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: true),
                    Longitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: true),
                    TimeZone = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ContactPhone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ContactEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    SafetyMeasures = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    HouseRules = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Facilities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Facilities_FacilityOwners_FacilityOwnerId",
                        column: x => x.FacilityOwnerId,
                        principalSchema: "dbo",
                        principalTable: "FacilityOwners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FacilityAmenities",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AmenityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FacilityAmenities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FacilityAmenities_Amenities_AmenityId",
                        column: x => x.AmenityId,
                        principalSchema: "dbo",
                        principalTable: "Amenities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FacilityAmenities_Facilities_FacilityId",
                        column: x => x.FacilityId,
                        principalSchema: "dbo",
                        principalTable: "Facilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FacilityOperatingHours",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DayOfWeek = table.Column<int>(type: "int", nullable: false),
                    OpensAt = table.Column<TimeOnly>(type: "time", nullable: true),
                    ClosesAt = table.Column<TimeOnly>(type: "time", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FacilityOperatingHours", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FacilityOperatingHours_Facilities_FacilityId",
                        column: x => x.FacilityId,
                        principalSchema: "dbo",
                        principalTable: "Facilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "dbo",
                table: "Amenities",
                columns: new[] { "Id", "Category", "CreatedAt", "DisplayOrder", "IsActive", "Key", "Name", "UpdatedAt" },
                values: new object[,]
                {
                    { new Guid("16557b82-105e-5195-826b-a606259bf214"), "Safety", new DateTimeOffset(new DateTime(2026, 9, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 30, true, "fire-extinguisher", "Fire extinguisher", null },
                    { new Guid("27bf719d-50f2-5e76-b392-1897b945e630"), "Comfort", new DateTimeOffset(new DateTime(2026, 9, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 10, true, "air-conditioning", "Air conditioning", null },
                    { new Guid("2ee9374d-1467-5426-a912-adebd6240cd0"), "Comfort", new DateTimeOffset(new DateTime(2026, 9, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 20, true, "showers", "Showers", null },
                    { new Guid("3c65a092-7ced-580e-a6bf-592029e433a4"), "Equipment", new DateTimeOffset(new DateTime(2026, 9, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 10, true, "racket-rental", "Racket rental", null },
                    { new Guid("595960f1-e817-56c6-be96-317fbe21687b"), "Access", new DateTimeOffset(new DateTime(2026, 9, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 30, true, "near-public-transport", "Near public transport", null },
                    { new Guid("5f10bf74-8ad5-51bb-9694-a12274375c3d"), "Comfort", new DateTimeOffset(new DateTime(2026, 9, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 60, true, "seating", "Spectator seating", null },
                    { new Guid("6015e3d0-73be-51ce-93e6-5dff6362fc79"), "Equipment", new DateTimeOffset(new DateTime(2026, 9, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 30, true, "shuttlecock-for-sale", "Shuttlecock for sale", null },
                    { new Guid("83b54e86-367e-5fea-a9c8-24fe0b87d58c"), "Access", new DateTimeOffset(new DateTime(2026, 9, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 20, true, "wheelchair-access", "Wheelchair access", null },
                    { new Guid("8e87c77b-a8c1-59c3-baf3-081595c7de44"), "Comfort", new DateTimeOffset(new DateTime(2026, 9, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 50, true, "drinking-water", "Drinking water", null },
                    { new Guid("a2d94510-2c91-575a-9256-4e17ec0c369c"), "Safety", new DateTimeOffset(new DateTime(2026, 9, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 50, true, "on-site-staff", "On-site staff", null },
                    { new Guid("a98dcb63-d4dc-5211-bf53-693946c22c84"), "Comfort", new DateTimeOffset(new DateTime(2026, 9, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 30, true, "lockers", "Lockers", null },
                    { new Guid("afe375ab-a228-5854-9aa7-03382439f85f"), "Comfort", new DateTimeOffset(new DateTime(2026, 9, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 40, true, "restrooms", "Restrooms", null },
                    { new Guid("b6cef6fb-0320-5670-9e7c-2bea98d4f6a4"), "Safety", new DateTimeOffset(new DateTime(2026, 9, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 10, true, "first-aid-kit", "First aid kit", null },
                    { new Guid("b94e2ec5-9db1-5be3-9891-67ded8fa9fcf"), "Access", new DateTimeOffset(new DateTime(2026, 9, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 10, true, "parking", "Parking", null },
                    { new Guid("c167c9f2-15ac-50a5-a552-22b436a0bff9"), "Safety", new DateTimeOffset(new DateTime(2026, 9, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 20, true, "cctv", "CCTV", null },
                    { new Guid("f0ace240-4169-5397-8de9-ceb8972b384c"), "Safety", new DateTimeOffset(new DateTime(2026, 9, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 40, true, "emergency-exit", "Emergency exit", null },
                    { new Guid("ffef0613-d0ac-5150-aec5-7f257da46a7c"), "Equipment", new DateTimeOffset(new DateTime(2026, 9, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 20, true, "ball-rental", "Ball rental", null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Amenities_Key",
                schema: "dbo",
                table: "Amenities",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Facilities_FacilityOwnerId",
                schema: "dbo",
                table: "Facilities",
                column: "FacilityOwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Facilities_Slug",
                schema: "dbo",
                table: "Facilities",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FacilityAmenities_AmenityId",
                schema: "dbo",
                table: "FacilityAmenities",
                column: "AmenityId");

            migrationBuilder.CreateIndex(
                name: "IX_FacilityAmenities_FacilityId_AmenityId",
                schema: "dbo",
                table: "FacilityAmenities",
                columns: new[] { "FacilityId", "AmenityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FacilityOperatingHours_FacilityId_DayOfWeek",
                schema: "dbo",
                table: "FacilityOperatingHours",
                columns: new[] { "FacilityId", "DayOfWeek" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FacilityAmenities",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "FacilityOperatingHours",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Amenities",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Facilities",
                schema: "dbo");
        }
    }
}
