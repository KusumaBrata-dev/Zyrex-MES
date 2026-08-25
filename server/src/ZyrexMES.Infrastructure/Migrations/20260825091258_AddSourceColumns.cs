using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZyrexMES.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "Stations",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Manual");

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "Routings",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Manual");

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "Products",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Manual");

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "Lines",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Manual");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Source",
                table: "Stations");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "Routings");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "Lines");
        }
    }
}
