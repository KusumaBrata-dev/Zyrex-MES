using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZyrexMES.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddScanDuplicateGuard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_UnitTransactions_UnitId_StationId_ScannedAtUtc",
                table: "UnitTransactions",
                columns: new[] { "UnitId", "StationId", "ScannedAtUtc" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UnitTransactions_UnitId_StationId_ScannedAtUtc",
                table: "UnitTransactions");
        }
    }
}
