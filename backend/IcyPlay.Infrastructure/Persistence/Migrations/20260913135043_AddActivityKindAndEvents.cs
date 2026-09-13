using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace IcyPlay.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddActivityKindAndEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                schema: "dbo",
                table: "Sports",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Sport");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "Sports",
                keyColumn: "Id",
                keyValue: new Guid("0d37f0cd-9517-5a92-a320-caff82b9bcc6"),
                column: "Kind",
                value: "Sport");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "Sports",
                keyColumn: "Id",
                keyValue: new Guid("10a110db-d802-51a0-ada1-8f81bc655dcd"),
                column: "Kind",
                value: "Sport");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "Sports",
                keyColumn: "Id",
                keyValue: new Guid("17b95f71-a2b5-50a7-9fde-b07562272bed"),
                column: "Kind",
                value: "Sport");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "Sports",
                keyColumn: "Id",
                keyValue: new Guid("2722a749-7853-5d82-a472-5c1ce07862c4"),
                column: "Kind",
                value: "Sport");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "Sports",
                keyColumn: "Id",
                keyValue: new Guid("441948f8-b6cf-5a23-8074-b45e692b2d73"),
                column: "Kind",
                value: "Sport");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "Sports",
                keyColumn: "Id",
                keyValue: new Guid("63573cf8-2b30-57f8-8ead-1da5098ea88e"),
                column: "Kind",
                value: "Sport");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "Sports",
                keyColumn: "Id",
                keyValue: new Guid("74ad5832-6d45-5076-8b4c-d0c79472dd55"),
                column: "Kind",
                value: "Sport");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "Sports",
                keyColumn: "Id",
                keyValue: new Guid("84ed5c9c-3978-56d8-bc1d-9cac5ae1109e"),
                column: "Kind",
                value: "Sport");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "Sports",
                keyColumn: "Id",
                keyValue: new Guid("8bf0905e-65ba-5600-a868-0f28087f1d0b"),
                column: "Kind",
                value: "Sport");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "Sports",
                keyColumn: "Id",
                keyValue: new Guid("8e21e167-3c31-5e9e-b203-f94339286f02"),
                column: "Kind",
                value: "Sport");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "Sports",
                keyColumn: "Id",
                keyValue: new Guid("a0f6b01f-491c-5bb0-ad8e-a42f19437ea3"),
                column: "Kind",
                value: "Sport");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "Sports",
                keyColumn: "Id",
                keyValue: new Guid("aee78495-e295-5753-8b47-de220d9d6f2b"),
                column: "Kind",
                value: "Sport");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "Sports",
                keyColumn: "Id",
                keyValue: new Guid("babc048a-e8a3-5572-8f03-c5a6a7f57d3f"),
                column: "Kind",
                value: "Sport");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "Sports",
                keyColumn: "Id",
                keyValue: new Guid("c1f88227-155e-5da6-b12b-479ed504e853"),
                column: "Kind",
                value: "Sport");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "Sports",
                keyColumn: "Id",
                keyValue: new Guid("dad7ebe3-f916-50f4-9317-92d01c75a061"),
                column: "Kind",
                value: "Sport");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "Sports",
                keyColumn: "Id",
                keyValue: new Guid("f22fc719-1486-5215-9cc9-ec04de119105"),
                column: "Kind",
                value: "Sport");

            migrationBuilder.InsertData(
                schema: "dbo",
                table: "Sports",
                columns: new[] { "Id", "Category", "CreatedAt", "DisplayOrder", "IsActive", "Key", "Kind", "Name", "UpdatedAt" },
                values: new object[,]
                {
                    { new Guid("1c4f8a2e-5b6d-5c7e-8f90-a1b2c3d4e5f6"), "Events", new DateTimeOffset(new DateTime(2026, 9, 11, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 10, true, "birthday-party", "Event", "Birthday party", null },
                    { new Guid("2d5a9b3f-6c7e-5d8f-9a01-b2c3d4e5f607"), "Events", new DateTimeOffset(new DateTime(2026, 9, 11, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 20, true, "corporate-event", "Event", "Corporate event", null },
                    { new Guid("3e6bac40-7d8f-5e90-ab12-c3d4e5f60718"), "Events", new DateTimeOffset(new DateTime(2026, 9, 11, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 30, true, "tournament", "Event", "Tournament", null },
                    { new Guid("4f7cbd51-8e90-5fa1-bc23-d4e5f6071829"), "Events", new DateTimeOffset(new DateTime(2026, 9, 11, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 40, true, "training-clinic", "Event", "Training clinic", null },
                    { new Guid("5a8dce62-9fa1-50b2-cd34-e5f60718293a"), "Events", new DateTimeOffset(new DateTime(2026, 9, 11, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 50, true, "concert-or-show", "Event", "Concert or show", null }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "Sports",
                keyColumn: "Id",
                keyValue: new Guid("1c4f8a2e-5b6d-5c7e-8f90-a1b2c3d4e5f6"));

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "Sports",
                keyColumn: "Id",
                keyValue: new Guid("2d5a9b3f-6c7e-5d8f-9a01-b2c3d4e5f607"));

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "Sports",
                keyColumn: "Id",
                keyValue: new Guid("3e6bac40-7d8f-5e90-ab12-c3d4e5f60718"));

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "Sports",
                keyColumn: "Id",
                keyValue: new Guid("4f7cbd51-8e90-5fa1-bc23-d4e5f6071829"));

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "Sports",
                keyColumn: "Id",
                keyValue: new Guid("5a8dce62-9fa1-50b2-cd34-e5f60718293a"));

            migrationBuilder.DropColumn(
                name: "Kind",
                schema: "dbo",
                table: "Sports");
        }
    }
}
