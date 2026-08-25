using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ZyrexMES.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUsersAndFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    FullName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UnitTransactions_UserId",
                table: "UnitTransactions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Repairs_ReportedByUserId",
                table: "Repairs",
                column: "ReportedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_QcResults_UserId",
                table: "QcResults",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_users_Username",
                table: "users",
                column: "Username",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_QcResults_users_UserId",
                table: "QcResults",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Repairs_users_ReportedByUserId",
                table: "Repairs",
                column: "ReportedByUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UnitTransactions_users_UserId",
                table: "UnitTransactions",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QcResults_users_UserId",
                table: "QcResults");

            migrationBuilder.DropForeignKey(
                name: "FK_Repairs_users_ReportedByUserId",
                table: "Repairs");

            migrationBuilder.DropForeignKey(
                name: "FK_UnitTransactions_users_UserId",
                table: "UnitTransactions");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropIndex(
                name: "IX_UnitTransactions_UserId",
                table: "UnitTransactions");

            migrationBuilder.DropIndex(
                name: "IX_Repairs_ReportedByUserId",
                table: "Repairs");

            migrationBuilder.DropIndex(
                name: "IX_QcResults_UserId",
                table: "QcResults");
        }
    }
}
