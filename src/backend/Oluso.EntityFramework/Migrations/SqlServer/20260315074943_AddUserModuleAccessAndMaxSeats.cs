using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oluso.EntityFramework.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class AddUserModuleAccessAndMaxSeats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AuthCredential",
                table: "WebhookEndpoints",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AuthType",
                table: "WebhookEndpoints",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SigningSecretEncrypted",
                table: "WebhookEndpoints",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxSeats",
                table: "TenantFeatures",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxSeats",
                table: "OrganizationFeatures",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UserModuleAccess",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    FeatureKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    GrantedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GrantedByUserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserModuleAccess", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserModuleAccess_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserModuleAccess_TenantId_FeatureKey",
                table: "UserModuleAccess",
                columns: new[] { "TenantId", "FeatureKey" });

            migrationBuilder.CreateIndex(
                name: "IX_UserModuleAccess_TenantId_UserId_FeatureKey",
                table: "UserModuleAccess",
                columns: new[] { "TenantId", "UserId", "FeatureKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserModuleAccess");

            migrationBuilder.DropColumn(
                name: "AuthCredential",
                table: "WebhookEndpoints");

            migrationBuilder.DropColumn(
                name: "AuthType",
                table: "WebhookEndpoints");

            migrationBuilder.DropColumn(
                name: "SigningSecretEncrypted",
                table: "WebhookEndpoints");

            migrationBuilder.DropColumn(
                name: "MaxSeats",
                table: "TenantFeatures");

            migrationBuilder.DropColumn(
                name: "MaxSeats",
                table: "OrganizationFeatures");
        }
    }
}
