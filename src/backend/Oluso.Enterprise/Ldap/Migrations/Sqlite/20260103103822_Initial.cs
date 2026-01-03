using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oluso.Enterprise.Ldap.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LdapServiceAccounts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    BindDn = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Permission = table.Column<int>(type: "INTEGER", nullable: false),
                    AllowedOus = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    AllowedIpRanges = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    MaxSearchResults = table.Column<int>(type: "INTEGER", nullable: false),
                    RateLimitPerMinute = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastUsedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LdapServiceAccounts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LdapServiceAccounts_BindDn",
                table: "LdapServiceAccounts",
                column: "BindDn",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LdapServiceAccounts_TenantId",
                table: "LdapServiceAccounts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_LdapServiceAccounts_TenantId_Name",
                table: "LdapServiceAccounts",
                columns: new[] { "TenantId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LdapServiceAccounts");
        }
    }
}
