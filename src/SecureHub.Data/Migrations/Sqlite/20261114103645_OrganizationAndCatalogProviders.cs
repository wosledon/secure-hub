using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecureHub.Data.Migrations.Sqlite
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
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Apps",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "CatalogProviders",
                columns: table => new
                {
                    ProviderKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Api = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Doc = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Npm = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    EnvJson = table.Column<string>(type: "TEXT", nullable: true),
                    ModelCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SyncedAt = table.Column<long>(type: "INTEGER", nullable: false)
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
