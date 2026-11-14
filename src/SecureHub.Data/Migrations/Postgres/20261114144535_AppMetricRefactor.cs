using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecureHub.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class AppMetricRefactor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AppName",
                table: "VirtualKeys",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TeamId",
                table: "VirtualKeys",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AppName",
                table: "UsageEvents",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AppName",
                table: "ContentBlobs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AppName",
                table: "AuditEvents",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "VirtualKeys"
                SET "AppName" = (SELECT LEFT("Name", 100) FROM "Apps" WHERE "Apps"."Id" = "VirtualKeys"."OwnerId"),
                    "TeamId" = (SELECT "TeamId" FROM "Apps" WHERE "Apps"."Id" = "VirtualKeys"."OwnerId")
                WHERE "OwnerType" = 0;

                UPDATE "UsageEvents"
                SET "AppName" = (SELECT LEFT("Name", 100) FROM "Apps" WHERE "Apps"."Id" = "UsageEvents"."AppId")
                WHERE "AppId" IS NOT NULL;

                UPDATE "ContentBlobs"
                SET "AppName" = (SELECT LEFT("Name", 100) FROM "Apps" WHERE "Apps"."Id" = "ContentBlobs"."AppId")
                WHERE "AppId" IS NOT NULL;

                UPDATE "VirtualKeys"
                SET "OwnerId" = '00000000-0000-0000-0000-000000000000'::uuid
                WHERE "OwnerType" = 0;

                UPDATE "Budgets"
                SET "Enabled" = FALSE
                WHERE "Subject" = 2;
                """);

            migrationBuilder.DropIndex(
                name: "IX_UsageEvents_AppId_OccurredAt",
                table: "UsageEvents");

            migrationBuilder.DropIndex(
                name: "IX_ContentBlobs_AppId_ExpiresAt",
                table: "ContentBlobs");

            migrationBuilder.DropColumn(
                name: "AppId",
                table: "UsageEvents");

            migrationBuilder.DropColumn(
                name: "AppId",
                table: "ContentBlobs");

            migrationBuilder.DropTable(
                name: "Apps");

            migrationBuilder.CreateIndex(
                name: "IX_VirtualKeys_AppName",
                table: "VirtualKeys",
                column: "AppName");

            migrationBuilder.CreateIndex(
                name: "IX_UsageEvents_AppName_OccurredAt",
                table: "UsageEvents",
                columns: new[] { "AppName", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentBlobs_AppName_ExpiresAt",
                table: "ContentBlobs",
                columns: new[] { "AppName", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_AppName_OccurredAt",
                table: "AuditEvents",
                columns: new[] { "AppName", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VirtualKeys_AppName",
                table: "VirtualKeys");

            migrationBuilder.DropIndex(
                name: "IX_UsageEvents_AppName_OccurredAt",
                table: "UsageEvents");

            migrationBuilder.DropIndex(
                name: "IX_ContentBlobs_AppName_ExpiresAt",
                table: "ContentBlobs");

            migrationBuilder.DropIndex(
                name: "IX_AuditEvents_AppName_OccurredAt",
                table: "AuditEvents");

            migrationBuilder.DropColumn(
                name: "AppName",
                table: "VirtualKeys");

            migrationBuilder.DropColumn(
                name: "TeamId",
                table: "VirtualKeys");

            migrationBuilder.DropColumn(
                name: "AppName",
                table: "UsageEvents");

            migrationBuilder.DropColumn(
                name: "AppName",
                table: "ContentBlobs");

            migrationBuilder.DropColumn(
                name: "AppName",
                table: "AuditEvents");

            migrationBuilder.AddColumn<Guid>(
                name: "AppId",
                table: "UsageEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AppId",
                table: "ContentBlobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Apps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Apps", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UsageEvents_AppId_OccurredAt",
                table: "UsageEvents",
                columns: new[] { "AppId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentBlobs_AppId_ExpiresAt",
                table: "ContentBlobs",
                columns: new[] { "AppId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Apps_Name",
                table: "Apps",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Apps_TeamId",
                table: "Apps",
                column: "TeamId");
        }
    }
}
