using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oluso.EntityFramework.Migrations.SqlServer
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
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SkipOwnClientEvents",
                table: "WebhookEndpoints",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginId",
                table: "AuditLogs",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginKind",
                table: "AuditLogs",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginName",
                table: "AuditLogs",
                type: "nvarchar(200)",
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
