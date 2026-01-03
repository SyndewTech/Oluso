using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oluso.Enterprise.Ldap.Migrations.Postgres
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
                    Id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    BindDn = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    Permission = table.Column<int>(type: "integer", nullable: false),
                    AllowedOus = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    AllowedIpRanges = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    MaxSearchResults = table.Column<int>(type: "integer", nullable: false),
                    RateLimitPerMinute = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
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
