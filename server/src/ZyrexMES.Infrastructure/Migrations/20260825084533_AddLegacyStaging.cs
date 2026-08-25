using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ZyrexMES.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLegacyStaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "legacy_line_snapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LegacyCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LegacyName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RawJson = table.Column<string>(type: "jsonb", nullable: false),
                    ImportedAtUtc = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_legacy_line_snapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "legacy_product_snapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LegacySku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LegacyName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RawJson = table.Column<string>(type: "jsonb", nullable: false),
                    ImportedAtUtc = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_legacy_product_snapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "legacy_routing_snapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LegacySku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    LegacyStationCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequireLabel = table.Column<bool>(type: "boolean", nullable: false),
                    RawJson = table.Column<string>(type: "jsonb", nullable: false),
                    ImportedAtUtc = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_legacy_routing_snapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "legacy_station_snapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LegacyLineCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LegacyCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LegacyName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProcessType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RawJson = table.Column<string>(type: "jsonb", nullable: false),
                    ImportedAtUtc = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_legacy_station_snapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "legacy_transaction_snapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SN = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StationCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResultChar = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    ScannedAtUtc = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    OperatorCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RawJson = table.Column<string>(type: "jsonb", nullable: false),
                    ImportedAtUtc = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_legacy_transaction_snapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_legacy_line_snapshots_LegacyCode",
                table: "legacy_line_snapshots",
                column: "LegacyCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_legacy_product_snapshots_LegacySku",
                table: "legacy_product_snapshots",
                column: "LegacySku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_legacy_station_snapshots_LegacyCode",
                table: "legacy_station_snapshots",
                column: "LegacyCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_legacy_transaction_snapshots_SN_StationCode_ScannedAtUtc",
                table: "legacy_transaction_snapshots",
                columns: new[] { "SN", "StationCode", "ScannedAtUtc" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "legacy_line_snapshots");

            migrationBuilder.DropTable(
                name: "legacy_product_snapshots");

            migrationBuilder.DropTable(
                name: "legacy_routing_snapshots");

            migrationBuilder.DropTable(
                name: "legacy_station_snapshots");

            migrationBuilder.DropTable(
                name: "legacy_transaction_snapshots");
        }
    }
}
