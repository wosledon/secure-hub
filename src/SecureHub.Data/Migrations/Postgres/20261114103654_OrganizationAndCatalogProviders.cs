using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecureHub.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class OrganizationAndCatalogProviders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Teams",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Apps",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "CatalogProviders",
                columns: table => new
                {
                    ProviderKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Api = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Doc = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Npm = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    EnvJson = table.Column<string>(type: "text", nullable: true),
                    ModelCount = table.Column<int>(type: "integer", nullable: false),
                    SyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogProviders", x => x.ProviderKey);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Teams_Name",
                table: "Teams",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CatalogProviders");

            migrationBuilder.DropIndex(
                name: "IX_Teams_Name",
                table: "Teams");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Teams");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Apps");
        }
    }
}
