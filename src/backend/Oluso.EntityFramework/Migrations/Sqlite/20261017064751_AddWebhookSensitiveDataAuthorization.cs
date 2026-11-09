using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oluso.EntityFramework.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AddWebhookSensitiveDataAuthorization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SensitiveDataAuthorizedAt",
                table: "WebhookEndpoints",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SensitiveDataAuthorizedBy",
                table: "WebhookEndpoints",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SensitiveDataAuthorizedByName",
                table: "WebhookEndpoints",
                type: "TEXT",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SensitiveDataAuthorizedAt",
                table: "WebhookEndpoints");

            migrationBuilder.DropColumn(
                name: "SensitiveDataAuthorizedBy",
                table: "WebhookEndpoints");

            migrationBuilder.DropColumn(
                name: "SensitiveDataAuthorizedByName",
                table: "WebhookEndpoints");
        }
    }
}
