using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oluso.EntityFramework.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class AddKeyRotationConfigs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KeyRotationConfigs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ClientId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    KeyType = table.Column<int>(type: "int", nullable: false),
                    Algorithm = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    KeySize = table.Column<int>(type: "int", nullable: false),
                    KeyLifetimeDays = table.Column<int>(type: "int", nullable: false),
                    RotationLeadDays = table.Column<int>(type: "int", nullable: false),
                    GracePeriodDays = table.Column<int>(type: "int", nullable: false),
                    MaxKeys = table.Column<int>(type: "int", nullable: false),
                    LastRotationAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NextRotationAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PreferredStorageProvider = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KeyRotationConfigs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KeyRotationConfigs_TenantId_ClientId",
                table: "KeyRotationConfigs",
                columns: new[] { "TenantId", "ClientId" },
                unique: true,
                filter: "[TenantId] IS NOT NULL AND [ClientId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KeyRotationConfigs");
        }
    }
}
