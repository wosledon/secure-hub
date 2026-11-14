using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecureHub.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class UsageLatencyAndErrors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DurationMs",
                table: "UsageEvents",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ErrorCode",
                table: "UsageEvents",
                type: "TEXT",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DurationMs",
                table: "UsageEvents");

            migrationBuilder.DropColumn(
                name: "ErrorCode",
                table: "UsageEvents");
        }
    }
}
