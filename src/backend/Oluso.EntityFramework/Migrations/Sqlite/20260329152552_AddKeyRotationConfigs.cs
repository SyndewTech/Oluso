using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oluso.EntityFramework.Migrations.Sqlite
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
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ClientId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    KeyType = table.Column<int>(type: "INTEGER", nullable: false),
                    Algorithm = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    KeySize = table.Column<int>(type: "INTEGER", nullable: false),
                    KeyLifetimeDays = table.Column<int>(type: "INTEGER", nullable: false),
                    RotationLeadDays = table.Column<int>(type: "INTEGER", nullable: false),
                    GracePeriodDays = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxKeys = table.Column<int>(type: "INTEGER", nullable: false),
                    LastRotationAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    NextRotationAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PreferredStorageProvider = table.Column<int>(type: "INTEGER", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KeyRotationConfigs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KeyRotationConfigs_TenantId_ClientId",
                table: "KeyRotationConfigs",
                columns: new[] { "TenantId", "ClientId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KeyRotationConfigs");
        }
    }
}
