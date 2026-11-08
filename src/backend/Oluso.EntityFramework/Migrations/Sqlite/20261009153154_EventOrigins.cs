using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oluso.EntityFramework.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class EventOrigins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClientId",
                table: "WebhookEndpoints",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SkipOwnClientEvents",
                table: "WebhookEndpoints",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginId",
                table: "AuditLogs",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginKind",
                table: "AuditLogs",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginName",
                table: "AuditLogs",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_OriginKind_OriginId",
                table: "AuditLogs",
                columns: new[] { "OriginKind", "OriginId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_OriginKind_OriginId",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "ClientId",
                table: "WebhookEndpoints");

            migrationBuilder.DropColumn(
                name: "SkipOwnClientEvents",
                table: "WebhookEndpoints");

            migrationBuilder.DropColumn(
                name: "OriginId",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "OriginKind",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "OriginName",
                table: "AuditLogs");
        }
    }
}
